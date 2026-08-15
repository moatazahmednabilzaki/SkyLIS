# Sky LIS end-to-end flow against the live API + PostgreSQL
$ErrorActionPreference = 'Stop'
$api = 'http://localhost:5178/api/v1'
$suffix = -join ((Get-Random -Count 6 -InputObject ([char[]]'abcdefghijklmnopqrstuvwxyz0123456789')))

function Step($name, $block) {
    try { $result = & $block; Write-Host ("PASS  {0}" -f $name); return $result }
    catch { Write-Host ("FAIL  {0}: {1}" -f $name, $_.Exception.Message); throw }
}

function ExpectError($name, $expectedStatus, $block) {
    try { & $block | Out-Null; Write-Host ("FAIL  {0}: expected HTTP {1} but call succeeded" -f $name, $expectedStatus) }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq $expectedStatus) { Write-Host ("PASS  {0} (HTTP {1} as expected)" -f $name, $status) }
        else { Write-Host ("FAIL  {0}: expected {1}, got {2}" -f $name, $expectedStatus, $status) }
    }
}

# ---------- Admin Portal flow ----------
$platformToken = (Step 'platform dev token' {
    Invoke-RestMethod -Method Post -Uri "$api/dev/token" -ContentType 'application/json' -Body '{"scope":"platform"}'
}).token
$ph = @{ Authorization = "Bearer $platformToken" }

$tenantA = (Step 'provision tenant A (NileLab)' {
    Invoke-RestMethod -Method Post -Uri "$api/platform/tenants" -Headers $ph -ContentType 'application/json' -Body (@{
        legalName = 'NileLab Diagnostics'; subdomain = "nilelab-$suffix"; countryCode = 'EG'
        planCode = 'PROFESSIONAL'; isolationTier = 'SharedRls' } | ConvertTo-Json)
}).id
$tenantB = (Step 'provision tenant B (Delta)' {
    Invoke-RestMethod -Method Post -Uri "$api/platform/tenants" -Headers $ph -ContentType 'application/json' -Body (@{
        legalName = 'Delta Medical Labs'; subdomain = "delta-$suffix"; countryCode = 'EG'
        planCode = 'STARTER'; isolationTier = 'SharedRls' } | ConvertTo-Json)
}).id

$directory = Step 'tenant directory lists both' {
    $list = Invoke-RestMethod -Uri "$api/platform/tenants" -Headers $ph
    if (($list | Where-Object { $_.id -in @($tenantA, $tenantB) }).Count -ne 2) { throw 'missing tenants' }
    $list
}
ExpectError 'duplicate subdomain rejected' 409 {
    Invoke-RestMethod -Method Post -Uri "$api/platform/tenants" -Headers $ph -ContentType 'application/json' -Body (@{
        legalName = 'Copycat'; subdomain = "nilelab-$suffix"; countryCode = 'EG'
        planCode = 'LITE'; isolationTier = 'SharedRls' } | ConvertTo-Json)
}

# ---------- Client Portal flow (tenant A) ----------
$tokenA = (Step 'tenant A dev token' {
    Invoke-RestMethod -Method Post -Uri "$api/dev/token" -ContentType 'application/json' -Body (@{
        scope = 'tenant'; tenantId = $tenantA } | ConvertTo-Json)
}).token
$ha = @{ Authorization = "Bearer $tokenA" }

$sampleType = Step 'create sample type with condition trees (P03.4)' {
    Invoke-RestMethod -Method Post -Uri "$api/catalog/sample-types" -Headers $ha -ContentType 'application/json' -Body (@{
        name = 'Venous blood'; containerName = 'Fluoride (grey)'
        conditions = @(
            @{ name = 'Random'; delayMinutes = $null; compatibilityGroup = 'VB-G1' },
            @{ name = 'Fasting 8h'; delayMinutes = $null; compatibilityGroup = 'VB-G1' },
            @{ name = 'Post-prandial +2h'; delayMinutes = 120; compatibilityGroup = 'VB-G2' }
        ) } | ConvertTo-Json -Depth 4)
}
$condFasting = ($sampleType.conditions | Where-Object name -eq 'Fasting 8h').id
$condRandom = ($sampleType.conditions | Where-Object name -eq 'Random').id
$condPp = ($sampleType.conditions | Where-Object name -eq 'Post-prandial +2h').id

function New-ActiveTest($code, $name, $conditionId, $price) {
    $test = Invoke-RestMethod -Method Post -Uri "$api/catalog/tests" -Headers $ha -ContentType 'application/json' -Body (@{
        code = $code; name = $name; department = 'Chemistry'; sampleTypeId = $sampleType.id
        requiredConditionId = $conditionId; price = $price; currency = 'EGP' } | ConvertTo-Json)
    Invoke-RestMethod -Method Post -Uri "$api/catalog/tests/$($test.id)/submit" -Headers $ha | Out-Null
    Invoke-RestMethod -Method Post -Uri "$api/catalog/tests/$($test.id)/approve" -Headers $ha | Out-Null
    $test.id
}
$gluF  = Step 'create+approve GLU-F (fasting)'  { New-ActiveTest 'GLU-F'  'Fasting Glucose' $condFasting 80 }
$hba1c = Step 'create+approve HBA1C (random)'   { New-ActiveTest 'HBA1C'  'HbA1c'           $condRandom 220 }
$gluPp = Step 'create+approve GLU-PP (PP +2h)'  { New-ActiveTest 'GLU-PP' 'Glucose PP 2h'   $condPp      80 }

$patient = (Step 'register patient (P04.2)' {
    Invoke-RestMethod -Method Post -Uri "$api/patients" -Headers $ha -ContentType 'application/json' -Body (@{
        fullName = 'Mona El-Sayed'; sex = 'Female'; dateOfBirth = '1992-03-10'
        mobile = '+201002345678'; nationalId = "2920310$suffix" } | ConvertTo-Json)
}).id

$search = Step 'patient search shows identity triple (P04.1)' {
    $hits = Invoke-RestMethod -Uri "$api/patients/search?term=Mona" -Headers $ha
    if ($hits.Count -lt 1 -or $hits[0].age -ne 34 -or $hits[0].gender -ne 'Female') { throw 'triple mismatch' }
    $hits[0]
}

$visit = Step 'register visit: consolidation + reservation (P05.2)' {
    $v = Invoke-RestMethod -Method Post -Uri "$api/visits" -Headers $ha -ContentType 'application/json' -Body (@{
        patientId = $patient; testIds = @($gluF, $hba1c, $gluPp); isStat = $false; statReason = $null } | ConvertTo-Json)
    if ($v.samples.Count -ne 2) { throw "expected 2 samples, got $($v.samples.Count)" }
    if ($v.total -ne 380) { throw "expected total 380, got $($v.total)" }
    if (-not ($v.samples | Where-Object state -eq 'ConditionPending')) { throw 'no reserved sample' }
    $v
}
$readySample = ($visit.samples | Where-Object state -eq 'ReadyToCollect').sampleId
$reservedSample = ($visit.samples | Where-Object state -eq 'ConditionPending').sampleId

ExpectError 'collecting reserved sample before window blocked' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$reservedSample/collect" -Headers $ha
}
Step 'collect ready sample (P08.2)' {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$readySample/collect" -Headers $ha
} | Out-Null
Step 'receive sample at accessioning (P07.2)' {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$readySample/receive" -Headers $ha
} | Out-Null

$recollection = Step 'reject sample -> recollection issued (P07.3)' {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$readySample/reject" -Headers $ha `
        -ContentType 'application/json' -Body '{"reasonCode":"HEMOLYZED"}'
}
$details = Step 'visit details: tests rebound to recollection (P05.3)' {
    $d = Invoke-RestMethod -Uri "$api/visits/$($visit.visitId)" -Headers $ha
    $rejected = $d.samples | Where-Object { $_.id -eq $readySample }
    if ($rejected.state -ne 'Rejected') { throw 'sample not rejected' }
    $rebound = $d.tests | Where-Object { $_.sampleId -eq $recollection.recollectionSampleId }
    if ($rebound.Count -ne 2) { throw "expected 2 rebound tests, got $($rebound.Count)" }
    $d
}

Step 'partial payment (P17.1)' {
    $p = Invoke-RestMethod -Method Post -Uri "$api/billing/invoices/$($visit.invoiceId)/payments" -Headers $ha `
        -ContentType 'application/json' -Body '{"amount":100,"currency":"EGP","method":"cash"}'
    if ($p.status -ne 'PartiallyPaid' -or $p.balance -ne 280) { throw "unexpected: $($p.status) / $($p.balance)" }
} | Out-Null
ExpectError 'overpayment beyond balance rejected' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/billing/invoices/$($visit.invoiceId)/payments" -Headers $ha `
        -ContentType 'application/json' -Body '{"amount":500,"currency":"EGP","method":"cash"}'
}
Step 'final payment -> Paid' {
    $p = Invoke-RestMethod -Method Post -Uri "$api/billing/invoices/$($visit.invoiceId)/payments" -Headers $ha `
        -ContentType 'application/json' -Body '{"amount":280,"currency":"EGP","method":"card"}'
    if ($p.status -ne 'Paid' -or $p.balance -ne 0) { throw "unexpected: $($p.status) / $($p.balance)" }
} | Out-Null
ExpectError 'paying a Paid invoice is a state conflict' 409 {
    Invoke-RestMethod -Method Post -Uri "$api/billing/invoices/$($visit.invoiceId)/payments" -Headers $ha `
        -ContentType 'application/json' -Body '{"amount":1,"currency":"EGP","method":"cash"}'
}

# ---------- Tenant isolation proof ----------
$tokenB = (Invoke-RestMethod -Method Post -Uri "$api/dev/token" -ContentType 'application/json' -Body (@{
    scope = 'tenant'; tenantId = $tenantB } | ConvertTo-Json)).token
$hb = @{ Authorization = "Bearer $tokenB" }

ExpectError "tenant B cannot read tenant A's visit (RLS + filters)" 404 {
    Invoke-RestMethod -Uri "$api/visits/$($visit.visitId)" -Headers $hb
}
Step "tenant B search finds no tenant A patients" {
    $hits = Invoke-RestMethod -Uri "$api/patients/search?term=Mona" -Headers $hb
    if ($hits.Count -ne 0) { throw "isolation breach: $($hits.Count) rows visible" }
} | Out-Null
ExpectError 'tenant token cannot use platform endpoints' 403 {
    Invoke-RestMethod -Uri "$api/platform/tenants" -Headers $ha
}

Write-Output ''
Write-Output ("E2E COMPLETE - visit {0}, invoice {1}, tenant A {2}, tenant B {3}" -f `
    $visit.visitNumber, $visit.invoiceNumber, $tenantA, $tenantB)
