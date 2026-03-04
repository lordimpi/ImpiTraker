param(
    [Parameter(Mandatory = $false)]
    [Alias("Host")]
    [string]$ServerHost = "127.0.0.1",

    [Parameter(Mandatory = $true)]
    [int]$Port,

    [Parameter(Mandatory = $true)]
    [string]$Payload,

    [Parameter(Mandatory = $false)]
    [int]$ReadTimeoutMs = 2000
)

$client = New-Object System.Net.Sockets.TcpClient
try {
    $client.Connect($ServerHost, $Port)
    $stream = $client.GetStream()
    $stream.ReadTimeout = $ReadTimeoutMs

    $bytes = [System.Text.Encoding]::ASCII.GetBytes($Payload)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()

    Start-Sleep -Milliseconds 150

    $buffer = New-Object byte[] 1024
    $ackText = ""
    try {
        if ($stream.DataAvailable) {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -gt 0) {
                $ackText = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read)
            }
        }
    }
    catch {
        $ackText = ""
    }

    Write-Host "Host: $ServerHost"
    Write-Host "Port: $Port"
    Write-Host "Payload sent: $Payload"
    if ([string]::IsNullOrWhiteSpace($ackText)) {
        Write-Host "ACK: (empty or timeout)"
    }
    else {
        Write-Host "ACK: $ackText"
    }
}
finally {
    if ($client -ne $null) {
        $client.Close()
    }
}
