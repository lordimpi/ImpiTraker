Role: runbook  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-19  
When to use: validate whether the unified API + TCP + SignalR host can sustain 5,000 to 10,000 GPS devices before introducing a message broker

# GPS Scale Test Plan

Canonical state note: this is an operational validation plan. For the current backend/runtime truth, use [`CURRENT_STATE.md`](CURRENT_STATE.md).

This runbook defines how to stress the platform with production-like GPS traffic so the team can answer the only question that matters: does the current unified host survive 5k-10k devices with acceptable latency and error rates, or are we hiding a bottleneck that forces architectural changes?

## 1) Objective

Validate the current architecture under realistic GPS load before adding RabbitMQ, Kafka, or other distributed complexity.

The test must prove or disprove all of the following:
- the unified `.NET` process (`ImpiTrack.Api` hosting HTTP + TCP + SignalR) can keep TCP ingestion stable while persisting data and pushing events in near real time;
- presence detection (10-minute offline threshold) does not create pathological backlog or noisy status churn under sustained load;
- SignalR fan-out to active frontend clients does not materially degrade ingestion or persistence throughput;
- the database tier can absorb the write rate without unacceptable latency growth or lock contention.

If the team cannot produce measured evidence for those points, then any discussion about brokers is hand-waving.

## 2) Load Model And Assumptions

### 2.1 Worst-case load

Assume `10,000` devices connected concurrently, each reporting every `10s`.

- TCP messages per second: about `1,000 msg/s`
- messages per minute: about `60,000`
- messages per hour: about `3.6M`

This is the red-line scenario. If the platform dies here, that is not automatically a failure if the real fleet shape is softer. But if the platform dies far below this point, stop fantasizing about production readiness.

### 2.2 Realistic mixed fleet

Use a mixed scenario that resembles actual GPS behavior instead of pretending every unit is moving constantly.

Recommended starting mix for `10,000` registered devices:
- `20%` moving devices (`2,000`) sending full tracking updates every `10s`
- `60%` parked but online devices (`6,000`) sending heartbeat or low-value status traffic every `60s`
- `20%` quiet devices (`2,000`) remaining connected with no meaningful traffic during the test window

Approximate effective input rate for that mix:
- moving traffic: `2,000 / 10s = 200 msg/s`
- parked traffic: `6,000 / 60s = 100 msg/s`
- total steady-state ingestion: about `300 msg/s`

This mixed scenario should be the main decision driver because it is much closer to how fleets behave in the real world.

### 2.3 SignalR load assumptions

Do not test TCP in isolation and then claim victory. SignalR is part of the architecture.

Run at least these frontend fan-out profiles during the same ingestion tests:
- `0` subscribers: baseline ingestion-only behavior
- `50` subscribers: normal internal operations usage
- `200` subscribers: aggressive ops/customer support view
- optional `500+` subscribers: only if the product actually expects broad live dashboards

Expected SignalR event shape:
- moving devices can trigger `PositionUpdated`
- online/offline transitions can trigger `DeviceStatusChanged`
- telemetry/event notifications may add additional fan-out depending on the notifier pipeline

Important: presence offline transitions are delayed by the `10-minute` threshold, so short tests will underrepresent offline fan-out. Any scenario that claims to validate presence at scale must run long enough to cross that threshold.

## 3) Metrics To Measure

Measure the whole path. If you only watch CPU, you are basically driving blindfolded.

### 3.1 Host and runtime

- CPU total and per core
- process working set / RSS / private bytes
- GC allocation rate, Gen 0/1/2 collections, pause time
- thread pool queue length and worker starvation signals
- active TCP connections
- socket errors / resets / disconnects

### 3.2 Ingestion pipeline

- packets received per second
- packets parsed per second
- successful inserts per second
- raw packet persistence latency (`p50`, `p95`, `p99`)
- normalized position persistence latency (`p50`, `p95`, `p99`)
- ACK latency back to device (`p50`, `p95`, `p99`) if measurable in the simulator
- backlog depth in any in-memory queue/channel
- dropped packets, parse errors, retry count, failed writes
- reconnect count per minute

### 3.3 SignalR

- connected clients
- sends per second
- per-event publish latency from persistence completion to hub send completion
- failed sends / disconnected clients / backpressure symptoms
- message size distribution if payload volume becomes material

### 3.4 Database

- inserts per second by relevant table (`raw_packets`, `positions`, and any event/status tables touched by the pipeline)
- transaction duration (`p50`, `p95`, `p99`)
- lock waits / deadlocks
- CPU, memory, IOPS, log write throughput on the DB host
- connection pool saturation / timeout count
- slow query count and top write statements by total time

## 4) Test Stages

Run the stages in order. Skipping straight to `10,000` is how people create noise instead of evidence.

### 4.1 Smoke stage - 100 devices

Purpose: verify the simulator, metrics, dashboards, and correctness under tiny load.

Run:
- `100` connected devices
- mixed traffic for `15-30 min`
- at least `10` SignalR subscribers

Exit goal:
- no systemic errors
- metrics visible end to end
- expected records persist correctly

### 4.2 Stage 1 - 1,000 devices

Purpose: validate the first meaningful concurrent load and catch obvious connection, pool, or queue limits.

Run:
- worst-case burst for `15 min`
- realistic mixed traffic for `30-60 min`
- `25-50` SignalR subscribers

Watch for:
- CPU spikes sustained above acceptable baseline
- queue growth that never drains
- DB latency beginning to curve upward

### 4.3 Stage 2 - 5,000 devices

Purpose: validate the lower bound of the target production range.

Run:
- realistic mixed traffic for `60-120 min`
- worst-case `10s` reporting for `15-30 min`
- `50-200` SignalR subscribers

Watch for:
- sustained memory growth
- GC pauses impacting ACK or persistence latency
- TCP reconnect storms
- write throughput flattening while latency climbs

### 4.4 Stage 3 - 10,000 devices

Purpose: validate the upper target and determine whether the current architecture still has margin.

Run:
- realistic mixed traffic for `2-4 h`
- worst-case load for at least `30 min`
- enough runtime to cross the `10-minute` presence threshold and observe online/offline churn behavior

Watch for:
- backlog that keeps increasing
- DB host saturation before API host saturation
- SignalR delivery lag materially behind ingress
- inability to recover after transient spikes

### 4.5 Spike and recovery

Purpose: prove the system can absorb ugly real-world behavior instead of only pretty steady-state traffic.

Run at least these patterns:
- `1,000 -> 5,000 -> 10,000` rapid connection ramp in minutes, not hours
- traffic burst to `2x` normal send rate for `5-10 min`
- forced disconnect of `10-20%` of simulators, then coordinated reconnect
- frontend subscriber spike while ingestion stays high

Success condition:
- the system degrades briefly, then returns to stable latency/backlog after the spike ends

## 5) Tooling

### 5.1 TCP device simulator

Use a purpose-built TCP load generator, not `Send-TcpPayload.ps1` in a loop like a caveman.

Minimum capabilities required:
- maintain thousands of concurrent TCP sockets
- schedule per-device cadence (`10s`, `60s`, burst mode)
- emit protocol-valid Coban/Cantrack payloads with unique IMEIs
- measure connect latency, send latency, ACK latency, disconnects, and reconnects
- support scenario files so runs are repeatable

Recommended implementation options:
- a dedicated `.NET` console tool under `ImpiTrack/Tools/` if the team wants protocol reuse from existing parsers/contracts
- or `k6` / custom Go / Node generator only if it can keep raw TCP connections stable and timestamp metrics correctly

What matters is not the language. What matters is whether the simulator lies.

### 5.2 .NET runtime and process metrics

Collect runtime metrics from the unified host with standard observability tooling:
- `dotnet-counters` for live CPU, GC, exceptions, thread pool, alloc rate
- `dotnet-trace` or equivalent only for focused deep dives, not every run
- structured application logs with per-stage counts and error reasons
- OS metrics for sockets, network throughput, and process memory

If OpenTelemetry / Prometheus metrics already exist in the environment, export the same counters there so runs can be compared historically.

### 5.3 Database metrics

Capture database evidence from the database host itself, not only from app logs.

At minimum:
- CPU, RAM, disk latency, transaction log pressure
- active sessions and waits
- insert throughput by table
- deadlocks / lock waits / timeout count
- slow write statements

If SQL Server is the target DB, use SQL Server DMVs and Query Store. If PostgreSQL is the target DB, use `pg_stat_activity`, `pg_stat_statements`, lock views, and storage metrics.

### 5.4 SignalR measurement

Measure SignalR with dedicated subscriber clients that:
- authenticate like real users
- join and remain connected for the entire scenario
- timestamp receipt of `PositionUpdated` / `DeviceStatusChanged`
- compute end-to-end lag from simulated send time or persistence completion marker

Do not infer SignalR health from "frontend looked fine". That is not a metric.

## 6) Execution Method

### 6.1 Pre-test checklist

- use an environment that resembles production sizing as closely as possible
- isolate background jobs, backup windows, and unrelated traffic
- freeze config for the duration of the campaign
- document exact app settings, DB size, retention settings, hardware, and network path
- pre-create the test device inventory and ownership mappings needed for SignalR fan-out

### 6.2 Test hygiene rules

- warm up the system before collecting numbers
- run each major scenario more than once
- change one variable at a time
- keep subscriber counts explicit in every report
- reset or archive DB evidence between runs so throughput is not polluted by old data interpretation
- record the exact start/end timestamps of every run

### 6.3 Duration guidance

- short smoke tests catch correctness problems
- `30-60 min` runs catch early saturation
- `2-4 h` runs catch memory creep, pool exhaustion, and delayed presence churn

If you only run a five-minute benchmark, you learned almost nothing.

## 7) Stage Success And Failure Criteria

Use explicit gates. "Se ve bien" is garbage.

### 7.1 Success criteria

For each completed stage, all of these should hold unless the team explicitly waives a criterion with evidence:
- no unbounded backlog growth in ingestion queues/channels
- no sustained error rate above `0.5%` of messages
- reconnect rate remains low and explainable, not storm-like
- persistence latency stays stable enough that `p95` does not drift upward throughout the run
- SignalR lag stays within the product expectation for live tracking during sustained load
- CPU and memory stabilize instead of trending endlessly upward
- DB waits remain controlled; no recurring deadlocks/timeouts pattern

Practical target thresholds to start with:
- smoke / `1,000`: `p95` persistence under `250 ms`, error rate under `0.1%`
- `5,000`: `p95` persistence under `500 ms`, SignalR lag `p95` under `2 s`, error rate under `0.3%`
- `10,000`: `p95` persistence under `1 s`, SignalR lag `p95` under `5 s`, error rate under `0.5%`, backlog drains after spikes

Tune those numbers when real product expectations are clearer, but do not remove them without replacing them. No threshold means no discipline.

### 7.2 Failure criteria

Any of these is a real failure signal:
- active connections collapse or flap repeatedly
- message backlog grows continuously for more than `10-15 min`
- DB timeouts, deadlocks, or pool exhaustion become recurring rather than isolated
- ACK latency or SignalR lag keeps increasing even after load stabilizes
- process memory keeps rising without leveling off
- presence monitor causes bursty offline/online noise that materially harms throughput

## 8) When RabbitMQ Or Kafka Actually Becomes Justified

Do not introduce a broker because it sounds enterprise. Introduce it when measurements say the current synchronous path is the bottleneck or the blast radius is unacceptable.

Consider RabbitMQ/Kafka seriously only when repeated tests show one or more of these patterns:
- TCP ingestion must stay fast, but DB or SignalR work causes persistent queueing in the unified host
- you need durable buffering during downstream outages, and in-memory backpressure is no longer enough
- the write path needs independent horizontal scaling from the connection-handling path
- retries and transient failures materially amplify latency for the whole process
- recovery after spikes requires decoupling producers from consumers to meet SLOs

Interpretation guidance:
- if the system handles `5k-10k` with headroom after normal tuning, DO NOT add a broker yet;
- if the system fails because the database is undersized, fixing the DB is cheaper than adding Kafka and keeping the same weak DB;
- if SignalR fan-out is the pain point, first evaluate notifier batching, event filtering, or separate realtime scaling before dropping a broker into the ingest path.

## 9) Anti-Self-Deception Rules

These are mandatory if you want believable numbers.

- Do not run the simulator and the system under test on the same constrained machine and then claim infrastructure saturation.
- Do not disable SignalR, logging, or persistence for the main test and then extrapolate to production.
- Do not average away spikes; keep `p95` and `p99` visible.
- Do not celebrate a single successful run; require repeatability.
- Do not hide the DB host metrics. Most "API bottlenecks" are actually storage bottlenecks.
- Do not test only happy-path connected devices. Include reconnects and ugly ramps.
- Do not shorten the run so much that the `10-minute` presence timeout never matters.

## 10) Recommended Deliverables Per Test Campaign

Every campaign should produce a short evidence pack with:
- scenario definition: device counts, cadence mix, subscriber count, duration
- environment definition: app version, config, hardware, DB engine/version
- metric snapshots: CPU, RAM, TCP connections, inserts/s, persistence latency, SignalR lag, errors, reconnects, backlog
- decision note: pass, conditional pass, or fail for each stage
- next action: tune config, scale infra, optimize DB, or consider architectural decoupling

If the team cannot hand this evidence pack to another engineer and reproduce the conclusion, the test campaign was sloppy.
