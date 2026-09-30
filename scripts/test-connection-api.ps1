param([string]$BaseUrl = 'http://127.0.0.1:5089/api/v1')
$ErrorActionPreference = 'Stop'
$checks = 0
function Assert-Response($Response, [int]$Status, [string]$Code) {
    if ($Response.StatusCode -ne $Status) { throw "Expected $Status, received $($Response.StatusCode)" }
    if ($Code -and ($Response.Content | ConvertFrom-Json).errorCode -ne $Code) { throw "Expected error code $Code" }
    if ($Response.Content -match 'StackTrace|Microsoft.Data.SqlClient.SqlException|test-password-marker') { throw 'Unsafe error response' }
    $script:checks++
}
$before = @(Invoke-RestMethod "$BaseUrl/database-connections").Count
$invalid = Invoke-WebRequest "$BaseUrl/database-connections/test" -Method Post -ContentType 'application/json' -Body '{"host":"","connectionTimeout":0}' -SkipHttpErrorCheck
Assert-Response $invalid 400 'VALIDATION_FAILED'
$missing = Invoke-WebRequest "$BaseUrl/database-connections/00000000-0000-0000-0000-000000000001" -SkipHttpErrorCheck
Assert-Response $missing 404 'NOT_FOUND'
$body = @{ name='Unreachable test'; provider='sqlserver'; host='127.0.0.1'; port=1; authenticationType='sqlserver'; username='test-user'; password='test-password-marker'; connectionTimeout=1; commandTimeout=1 } | ConvertTo-Json
foreach ($endpoint in @('test', 'discover', '')) {
    $response = Invoke-WebRequest "$BaseUrl/database-connections/$endpoint" -Method Post -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
    Assert-Response $response 422 'CONNECTION_FAILED'
}
$newInline = Invoke-WebRequest "$BaseUrl/jobs" -Method Post -ContentType 'application/json' -Body '{"name":"invalid-inline","sqlServer":{"server":"test"},"databases":["db"],"backupDirectory":"test"}' -SkipHttpErrorCheck
Assert-Response $newInline 400 'VALIDATION_FAILED'
$invalidJob = Invoke-WebRequest "$BaseUrl/jobs" -Method Post -ContentType 'application/json' -Body '{"name":"","databases":[],"backupDirectory":"","retentionDays":0}' -SkipHttpErrorCheck
Assert-Response $invalidJob 400 'VALIDATION_FAILED'
$malformed = Invoke-WebRequest "$BaseUrl/database-connections/test" -Method Post -ContentType 'application/json' -Body '{' -SkipHttpErrorCheck
Assert-Response $malformed 400 ''
$after = @(Invoke-RestMethod "$BaseUrl/database-connections").Count
if ($before -ne $after) { throw 'Failed requests persisted a connection' }
Write-Output "$checks HTTP checks passed; failures did not persist connections. Real SqlClient requests used loopback port 1 (unreachable)."
