# Runbooks

Role: index  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15

Runbooks are repeatable procedures. They are not the canonical architecture snapshot.

Required metadata for runbooks:

- `Role: runbook`
- `Status: active` or `Status: historical`
- `Owner:` responsible team or maintainer
- `Last Reviewed:` date
- `When to use:` trigger for the procedure

Current active runbooks:

- [`../GPS_SCALE_TEST_PLAN.md`](../GPS_SCALE_TEST_PLAN.md) - staged load and scale validation plan for 5,000 to 10,000 GPS devices on the unified host
- [`../TCP_API_E2E_GUIDE.md`](../TCP_API_E2E_GUIDE.md) - end-to-end backend validation flow
- [`../MANUAL_E2E_EXECUTION_PLAN.md`](../MANUAL_E2E_EXECUTION_PLAN.md) - step-by-step manual E2E procedure
- [`SMTP_GMAIL_SETUP.md`](SMTP_GMAIL_SETUP.md) - Gmail SMTP setup for real email delivery in development or controlled environments

If a runbook starts explaining the whole system state, move that content back to [`../CURRENT_STATE.md`](../CURRENT_STATE.md).
