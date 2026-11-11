<#
.SYNOPSIS
  Generates concurrent traffic against the demo API: successes, failures, slow requests, DB and workflow calls.
.EXAMPLE
  ./scripts/load-test.ps1 -BaseUrl https://<app>.azurewebsites.net -Rounds 20 -Concurrency 8
#>
param(
    [string]$BaseUrl = "http://localhost:5098",
    [int]$Rounds = 10,
    [int]$Concurrency = 5,
    [int]$SlowDelayMs = 2000
)

Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(60)
$BaseUrl = $BaseUrl.TrimEnd('/')

function Json($o) { [System.Net.Http.StringContent]::new(($o | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, "application/json") }

# Each scenario returns a Task producing "name status".
function Send($name, $method, $path, $body) {
    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$BaseUrl$path")
    $req.Headers.Add("X-Correlation-ID", "load-" + [guid]::NewGuid().ToString("N").Substring(0, 12))
    if ($body) { $req.Content = Json $body }
    $client.SendAsync($req)  | ForEach-Object { [pscustomobject]@{ Name = $name; Task = $_ } }
}

$scenarios = @(
    { Send "orders.list"      GET  "/api/orders" $null },
    { Send "orders.create"    POST "/api/orders" @{ productSku = "LOAD-$(Get-Random -Max 99)"; quantity = 2; amount = 19.99 } },
    { Send "orders.invalid"   POST "/api/orders" @{ productSku = ""; quantity = 0; amount = -1 } },            # 400
    { Send "payments.create"  POST "/api/payments" @{ amount = 25; currency = "USD" } },
    { Send "payments.failure" POST "/api/payments/failure" $null },                                            # 502
    { Send "monitoring.exception" GET "/api/monitoring/exception" $null },                                     # 500
    { Send "monitoring.slow"  GET  "/api/monitoring/slow?delayMs=$SlowDelayMs" $null },
    { Send "monitoring.database" GET "/api/monitoring/database" $null },
    { Send "monitoring.external" GET "/api/monitoring/external-call" $null },
    { Send "monitoring.workflow" GET "/api/monitoring/workflow" $null }
)

$results = @{}
$sw = [Diagnostics.Stopwatch]::StartNew()
for ($r = 1; $r -le $Rounds; $r++) {
    $pending = @()
    for ($c = 0; $c -lt $Concurrency; $c++) {
        $s = $scenarios[(($r * $Concurrency) + $c) % $scenarios.Count]
        $pending += & $s
    }
    foreach ($p in $pending) {
        try {
            $resp = $p.Task.GetAwaiter().GetResult()
            $key = "$($p.Name) -> $([int]$resp.StatusCode)"
        } catch { $key = "$($p.Name) -> ERROR" }
        $results[$key] = 1 + [int]$results[$key]
    }
    Write-Host "round $r/$Rounds done"
}
$sw.Stop()
Write-Host "`nFinished in $([math]::Round($sw.Elapsed.TotalSeconds,1))s"
$results.GetEnumerator() | Sort-Object Name | Format-Table @{n="Scenario -> Status";e={$_.Name}}, @{n="Count";e={$_.Value}} -AutoSize
