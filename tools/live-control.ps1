param(
    [Parameter(Mandatory = $true)]
    [string]$Json,

    [string]$HostName = "127.0.0.1",
    [int]$Port = 40501,
    [int]$TimeoutMs = 8000
)

$client = [System.Net.Sockets.TcpClient]::new()
$connect = $client.BeginConnect($HostName, $Port, $null, $null)
if (-not $connect.AsyncWaitHandle.WaitOne($TimeoutMs)) {
    $client.Close()
    throw "Timed out connecting to ${HostName}:${Port}"
}

$client.EndConnect($connect)
$client.ReceiveTimeout = $TimeoutMs
$client.SendTimeout = $TimeoutMs

try {
    $stream = $client.GetStream()
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false), 1024, $true)
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $false, 1024, $true)
    $writer.NewLine = "`n"
    $writer.AutoFlush = $true
    $writer.WriteLine($Json)
    $reader.ReadLine()
}
finally {
    if ($reader) { $reader.Dispose() }
    if ($writer) { $writer.Dispose() }
    $client.Close()
}


