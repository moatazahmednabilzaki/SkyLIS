# Sky LIS end-to-end flow against the live API + PostgreSQL
# Cross-platform: override the psql binary/port/password via SKYLIS_* env vars (CI uses
# the postgres service container on 5432; local dev uses the D: cluster on 5433).
$ErrorActionPreference = 'Stop'
$api = if ($env:SKYLIS_API) { $env:SKYLIS_API } else { 'http://localhost:5178/api/v1' }
$psql = if ($env:SKYLIS_PSQL) { $env:SKYLIS_PSQL } else { 'C:\Program Files\PostgreSQL\17\bin\psql.exe' }
$pgPort = if ($env:SKYLIS_PGPORT) { $env:SKYLIS_PGPORT } else { '5433' }
$env:PGPASSWORD = if ($env:SKYLIS_PGPASSWORD) { $env:SKYLIS_PGPASSWORD } else { 'postgres_dev_only' }
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
        planCode = 'PROFESSIONAL'; isolationTier = 'SharedRls'
        adminUserName = 'sara.hassan'; adminFullName = 'Dr. Sara Hassan'; adminPassword = 'NileLab#Dev2026!' } | ConvertTo-Json)
}).id
$tenantB = (Step 'provision tenant B (Delta)' {
    Invoke-RestMethod -Method Post -Uri "$api/platform/tenants" -Headers $ph -ContentType 'application/json' -Body (@{
        legalName = 'Delta Medical Labs'; subdomain = "delta-$suffix"; countryCode = 'EG'
        planCode = 'STARTER'; isolationTier = 'SharedRls'
        adminUserName = 'delta.admin'; adminFullName = 'Delta Admin'; adminPassword = 'DeltaLab#Dev2026!' } | ConvertTo-Json)
}).id

$directory = Step 'tenant directory lists both' {
    $list = Invoke-RestMethod -Uri "$api/platform/tenants" -Headers $ph
    if (($list | Where-Object { $_.id -in @($tenantA, $tenantB) }).Count -ne 2) { throw 'missing tenants' }
    $list
}
ExpectError 'duplicate subdomain rejected' 409 {
    Invoke-RestMethod -Method Post -Uri "$api/platform/tenants" -Headers $ph -ContentType 'application/json' -Body (@{
        legalName = 'Copycat'; subdomain = "nilelab-$suffix"; countryCode = 'EG'
        planCode = 'LITE'; isolationTier = 'SharedRls'
        adminUserName = 'copy.admin'; adminFullName = 'Copy Admin'; adminPassword = 'CopycatLab#2026!' } | ConvertTo-Json)
}

Step 'country packs: EG pack ships with the platform (P01.4)' {
    $packs = Invoke-RestMethod -Uri "$api/platform/country-packs" -Headers $ph
    $eg = $packs | Where-Object countryCode -eq 'EG'
    if (-not $eg) { throw 'EG country pack missing' }
    if ($eg.sampleTypes.Count -lt 4) { throw "expected >=4 pack sample types, got $($eg.sampleTypes.Count)" }
} | Out-Null

# ---------- Client Portal flow (tenant A) ----------
$tokenA = (Step 'tenant A dev token' {
    Invoke-RestMethod -Method Post -Uri "$api/dev/token" -ContentType 'application/json' -Body (@{
        scope = 'tenant'; tenantId = $tenantA } | ConvertTo-Json)
}).token
$ha = @{ Authorization = "Bearer $tokenA" }

$branch = Step 'MAIN branch + EG defaults seeded via outbox (P03.2 / FR-TEN-040)' {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 2
        $branches = Invoke-RestMethod -Uri "$api/org/branches" -Headers $ha
        $types = Invoke-RestMethod -Uri "$api/catalog/sample-types" -Headers $ha
        $main = $branches | Where-Object code -eq 'MAIN'
    } while ((-not $main -or $types.Count -lt 4) -and (Get-Date) -lt $deadline)
    if (-not $main -or -not $main.isMain) { throw 'MAIN branch not seeded' }
    if (-not ($types | Where-Object name -eq 'Serum')) { throw 'EG pack sample taxonomy not seeded' }
    if (-not (($types | Where-Object name -eq 'Serum').conditions | Where-Object name -eq 'Fasting 8h')) {
        throw 'seeded Serum type missing its condition tree'
    }
    $main
}

Step 'add department to MAIN (P03.2)' {
    Invoke-RestMethod -Method Post -Uri "$api/org/branches/$($branch.id)/departments" -Headers $ha `
        -ContentType 'application/json' -Body '{"code":"CHEM","name":"Chemistry"}' | Out-Null
    $b = (Invoke-RestMethod -Uri "$api/org/branches" -Headers $ha) | Where-Object code -eq 'MAIN'
    if (-not ($b.departments | Where-Object code -eq 'CHEM')) { throw 'department not listed' }
} | Out-Null
ExpectError 'duplicate department code rejected' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/org/branches/$($branch.id)/departments" -Headers $ha `
        -ContentType 'application/json' -Body '{"code":"CHEM","name":"Chemistry again"}'
}

$zamalek = Step 'open second branch ZMLK (P03.2)' {
    $created = Invoke-RestMethod -Method Post -Uri "$api/org/branches" -Headers $ha -ContentType 'application/json' `
        -Body '{"code":"zmlk","name":"Zamalek Branch","address":"26 July St.","phone":"+20221234567"}'
    $b = (Invoke-RestMethod -Uri "$api/org/branches" -Headers $ha) | Where-Object id -eq $created.id
    if ($b.code -ne 'ZMLK') { throw "expected normalized code ZMLK, got $($b.code)" }
    $b
}

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

$visit = Step 'register visit: consolidation + reservation + branch numbering (P05.2)' {
    $v = Invoke-RestMethod -Method Post -Uri "$api/visits" -Headers $ha -ContentType 'application/json' -Body (@{
        patientId = $patient; branchId = $branch.id; testIds = @($gluF, $hba1c, $gluPp)
        isStat = $false; statReason = $null } | ConvertTo-Json)
    if ($v.samples.Count -ne 2) { throw "expected 2 samples, got $($v.samples.Count)" }
    if ($v.total -ne 380) { throw "expected total 380, got $($v.total)" }
    if (-not ($v.samples | Where-Object state -eq 'ConditionPending')) { throw 'no reserved sample' }
    if ($v.visitNumber -notmatch '^V-MAIN-\d{6}-0001$') { throw "expected V-MAIN-…-0001, got $($v.visitNumber)" }
    if ($v.invoiceNumber -notmatch '^INV-MAIN-') { throw "expected INV-MAIN-…, got $($v.invoiceNumber)" }
    $v
}
ExpectError 'registering a visit without a branch is rejected' 400 {
    Invoke-RestMethod -Method Post -Uri "$api/visits" -Headers $ha -ContentType 'application/json' -Body (@{
        patientId = $patient; testIds = @($gluF); isStat = $false; statReason = $null } | ConvertTo-Json)
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

# ---------- M08: merged role worklists ----------
Step 'reception worklist: rejection needs patient information (P08.1)' {
    $wl = Invoke-RestMethod -Uri "$api/worklists/reception" -Headers $ha
    $item = $wl.patientInformation | Where-Object sampleId -eq $readySample
    if (-not $item) { throw 'rejected sample missing from patient-information queue' }
    if ($item.reasonCode -ne 'HEMOLYZED') { throw "wrong reason: $($item.reasonCode)" }
    if (-not ($wl.reservationsDue | Where-Object sampleId -eq $reservedSample)) { throw 'PP reservation missing' }
} | Out-Null

Step 'phlebotomist worklist: recollection queued, reservation upcoming (P08.2)' {
    $wl = Invoke-RestMethod -Uri "$api/worklists/phlebotomist" -Headers $ha
    $reco = $wl.toCollect | Where-Object sampleId -eq $recollection.recollectionSampleId
    if (-not $reco -or -not $reco.isRecollection) { throw 'recollection not in the queue' }
    if (-not ($wl.upcomingReservations | Where-Object sampleId -eq $reservedSample)) { throw 'PP reservation not upcoming' }
} | Out-Null

Step 'mark patient informed (P07.3 mandatory step) clears the queue' {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$readySample/mark-informed" -Headers $ha | Out-Null
    $wl = Invoke-RestMethod -Uri "$api/worklists/reception" -Headers $ha
    if ($wl.patientInformation | Where-Object sampleId -eq $readySample) { throw 'still in queue after informing' }
} | Out-Null
ExpectError 'informing twice is rejected' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$readySample/mark-informed" -Headers $ha
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

# ---------- M09: Results Entry & Validation ----------
# Configure result schemas (P03.3 result-schema tab)
Step 'set result schema GLU-F (auto-verify on)' {
    Invoke-RestMethod -Method Put -Uri "$api/catalog/tests/$gluF/result-schema" -Headers $ha -ContentType 'application/json' -Body (@{
        unit = 'mg/dL'; refLow = 70; refHigh = 100; criticalLow = 40; criticalHigh = 400
        absurdLow = 5; absurdHigh = 1500; autoVerify = $true; deltaThresholdPercent = 50 } | ConvertTo-Json)
} | Out-Null
Step 'set result schema HBA1C (no auto-verify)' {
    Invoke-RestMethod -Method Put -Uri "$api/catalog/tests/$hba1c/result-schema" -Headers $ha -ContentType 'application/json' -Body (@{
        unit = '%'; refLow = 4; refHigh = 5.6; criticalLow = $null; criticalHigh = $null
        absurdLow = 2; absurdHigh = 25; autoVerify = $false; deltaThresholdPercent = $null } | ConvertTo-Json)
} | Out-Null
Step 'set result schema GLU-PP' {
    Invoke-RestMethod -Method Put -Uri "$api/catalog/tests/$gluPp/result-schema" -Headers $ha -ContentType 'application/json' -Body (@{
        unit = 'mg/dL'; refLow = 70; refHigh = 140; criticalLow = 40; criticalHigh = 400
        absurdLow = 5; absurdHigh = 1500; autoVerify = $true; deltaThresholdPercent = 50 } | ConvertTo-Json)
} | Out-Null

# Receive the recollected sample so its two lines allow entry
$detailsNow = Invoke-RestMethod -Uri "$api/visits/$($visit.visitId)" -Headers $ha
$recollSample = ($detailsNow.samples | Where-Object { $_.id -eq $recollection.recollectionSampleId })
Step 'collect + receive the recollection sample' {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$($recollSample.id)/collect" -Headers $ha | Out-Null
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/samples/$($recollSample.id)/receive" -Headers $ha | Out-Null
} | Out-Null

$gluLine = ($detailsNow.tests | Where-Object testCode -eq 'GLU-F')
$hbaLine = ($detailsNow.tests | Where-Object testCode -eq 'HBA1C')
$ppLine  = ($detailsNow.tests | Where-Object testCode -eq 'GLU-PP')

ExpectError 'absurd value cannot be saved' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $gluLine.id; value = 9000 } | ConvertTo-Json)
}
ExpectError 'entry blocked while sample not received (reserved PP)' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $ppLine.id; value = 120 } | ConvertTo-Json)
}

$gluResult = Step 'enter clean GLU-F -> auto-verified (P09.1)' {
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $gluLine.id; value = 92 } | ConvertTo-Json)
    if (-not $r.autoVerified -or $r.flag -ne 'Normal') { throw "expected auto-verified Normal, got $($r.status)/$($r.flag)" }
    $r
}
$hbaResult = Step 'enter high HBA1C -> technical queue' {
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $hbaLine.id; value = 8.4 } | ConvertTo-Json)
    if ($r.autoVerified -or $r.flag -ne 'High') { throw "expected non-auto High, got $($r.status)/$($r.flag)" }
    $r
}
ExpectError 'duplicate entry for the same line rejected' 409 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $gluLine.id; value = 95 } | ConvertTo-Json)
}

Step 'technical queue shows HBA1C; supervisor accepts (P09.2)' {
    $queue = Invoke-RestMethod -Uri "$api/results/technical-queue" -Headers $ha
    if (-not ($queue | Where-Object resultId -eq $hbaResult.resultId)) { throw 'HBA1C missing from technical queue' }
    Invoke-RestMethod -Method Post -Uri "$api/results/$($hbaResult.resultId)/accept-technical" -Headers $ha | Out-Null
} | Out-Null

ExpectError 'SoD: enterer cannot medically validate own result' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($gluResult.resultId)/validate-medical" -Headers $ha `
        -ContentType 'application/json' -Body '{"interpretiveComment":null,"signatureIntent":"I validate"}'
}

# A different user (fresh dev token = new user id) signs out both results
$tokenDoctor = (Invoke-RestMethod -Method Post -Uri "$api/dev/token" -ContentType 'application/json' -Body (@{
    scope = 'tenant'; tenantId = $tenantA; userName = 'dev-lab-director' } | ConvertTo-Json)).token
$hd = @{ Authorization = "Bearer $tokenDoctor" }
Step 'medical sign-out by a different user (P09.3, e-signature)' {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($gluResult.resultId)/validate-medical" -Headers $hd `
        -ContentType 'application/json' -Body '{"interpretiveComment":null,"signatureIntent":"I medically validate GLU-F = 92 mg/dL"}' | Out-Null
    Invoke-RestMethod -Method Post -Uri "$api/results/$($hbaResult.resultId)/validate-medical" -Headers $hd `
        -ContentType 'application/json' -Body '{"interpretiveComment":"Consistent with poor glycemic control.","signatureIntent":"I medically validate HBA1C = 8.4 %"}' | Out-Null
} | Out-Null

# Critical value on the PP sample: collect after the window would take 2h, so verify the
# critical path on a fresh STAT visit with an immediate-collection test instead.
$visit2 = Step 'second visit for the critical-value path (branch series advances)' {
    $v = Invoke-RestMethod -Method Post -Uri "$api/visits" -Headers $ha -ContentType 'application/json' -Body (@{
        patientId = $patient; branchId = $branch.id; testIds = @($gluF)
        isStat = $true; statReason = 'ER request' } | ConvertTo-Json)
    if ($v.visitNumber -notmatch '-0002$') { throw "expected MAIN series -0002, got $($v.visitNumber)" }
    $v
}
$v2sample = $visit2.samples[0].sampleId
Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/samples/$v2sample/collect" -Headers $ha | Out-Null
Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/samples/$v2sample/receive" -Headers $ha | Out-Null
$v2line = (Invoke-RestMethod -Uri "$api/visits/$($visit2.visitId)" -Headers $ha).tests[0].id

$critical = Step 'critical low glucose flagged, never auto-verifies (P09.4)' {
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $v2line; value = 32 } | ConvertTo-Json)
    if (-not $r.criticalFlagged -or $r.autoVerified) { throw "expected critical non-auto, got $($r | ConvertTo-Json -Compress)" }
    $r
}
Step 'call without read-back keeps the critical open' {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($critical.resultId)/critical/document-call" -Headers $ha `
        -ContentType 'application/json' -Body '{"calledPerson":"Dr. Hossam Fathy","phone":"+201224567890","readBackConfirmed":false}' | Out-Null
    $q = Invoke-RestMethod -Uri "$api/results/critical-queue" -Headers $ha
    if (($q | Where-Object resultId -eq $critical.resultId).criticalState -ne 'ReadBackDocumented') { throw 'expected open (ReadBackDocumented)' }
} | Out-Null
Step 'read-back confirmed closes the critical value' {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($critical.resultId)/critical/document-call" -Headers $ha `
        -ContentType 'application/json' -Body '{"calledPerson":"Dr. Hossam Fathy","phone":"+201224567890","readBackConfirmed":true}' | Out-Null
    $q = Invoke-RestMethod -Uri "$api/results/critical-queue" -Headers $ha
    if (($q | Where-Object resultId -eq $critical.resultId).criticalState -ne 'Closed') { throw 'expected Closed' }
} | Out-Null

Step 'rerun voids a result and reopens the line' {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($critical.resultId)/rerun" -Headers $ha `
        -ContentType 'application/json' -Body '{"reason":"specimen integrity check"}' | Out-Null
    $line = (Invoke-RestMethod -Uri "$api/visits/$($visit2.visitId)" -Headers $ha).tests[0]
    if ($line.status -ne 'Pending') { throw "expected Pending line, got $($line.status)" }
} | Out-Null

# ---------- M10: Reporting & Delivery ----------
# Complete visit2: re-enter after rerun (auto-verify) and sign out -> Validated
Step 'visit2: re-enter after rerun, sign out -> Validated' {
    $r2 = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/results" -Headers $ha `
        -ContentType 'application/json' -Body (@{ visitTestId = $v2line; value = 88 } | ConvertTo-Json)
    if (-not $r2.autoVerified) { throw 'expected auto-verify' }
    Invoke-RestMethod -Method Post -Uri "$api/results/$($r2.resultId)/validate-medical" -Headers $hd `
        -ContentType 'application/json' -Body '{"interpretiveComment":null,"signatureIntent":"I medically validate GLU-F = 88 mg/dL"}' | Out-Null
    $v = Invoke-RestMethod -Uri "$api/visits/$($visit2.visitId)" -Headers $ha
    if ($v.status -ne 'Validated') { throw "expected Validated, got $($v.status)" }
} | Out-Null

ExpectError 'FINAL blocked on partially validated visit1 (interim required)' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Final"}'
}
$interim = Step 'INTERIM renders for visit1 (validated subset)' {
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Interim"}'
    if ($r.kind -ne 'Interim' -or $r.version -ne 1) { throw "unexpected $($r.kind) v$($r.version)" }
    $r
}

$final = Step 'FINAL renders for visit2 -> visit Reported + metering event' {
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Final"}'
    $v = Invoke-RestMethod -Uri "$api/visits/$($visit2.visitId)" -Headers $ha
    if ($v.status -ne 'Reported') { throw "expected Reported, got $($v.status)" }
    $r
}
ExpectError 'second FINAL for the same visit rejected' 409 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit2.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Final"}'
}

Step 'report artifact is retrievable and hash-stable' {
    # Hash over the RAW bytes (the artifact contains UTF-8 Arabic text).
    $response = Invoke-WebRequest -Uri "$api/reports/$($final.reportId)/content" -Headers $ha -UseBasicParsing
    $stream = New-Object System.IO.MemoryStream
    $response.RawContentStream.CopyTo($stream)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = [BitConverter]::ToString($sha.ComputeHash($stream.ToArray())).Replace('-','')
    if ($hash -ne $final.contentHash) { throw "artifact hash mismatch: $hash vs $($final.contentHash)" }
} | Out-Null

Step 'delivery via whatsapp logs attempt and marks Delivered' {
    $d = Invoke-RestMethod -Method Post -Uri "$api/reports/$($final.reportId)/deliver" -Headers $ha `
        -ContentType 'application/json' -Body '{"channel":"whatsapp","destination":"+201002345678"}'
    if ($d.outcome -ne 'Sent' -or $d.reportStatus -ne 'Delivered') { throw "unexpected $($d | ConvertTo-Json -Compress)" }
} | Out-Null

Step 'public verification: valid hash, initials only, no PHI (anonymous)' {
    $v = Invoke-RestMethod -Uri "$api/public/reports/$($final.reportId)/verify?hash=$($final.contentHash)"
    if (-not $v.found -or -not $v.hashValid) { throw 'expected valid' }
    if ($v.patientInitials -ne 'M.E.') { throw "expected initials M.E., got $($v.patientInitials)" }
    if (($v | ConvertTo-Json) -match 'Mona') { throw 'PHI leaked in public verification!' }
} | Out-Null
Step 'public verification: tampered hash detected' {
    $v = Invoke-RestMethod -Uri "$api/public/reports/$($final.reportId)/verify?hash=DEADBEEF"
    if ($v.hashValid) { throw 'tampered hash accepted!' }
} | Out-Null

# Critical gate: a FINAL cannot render while a critical value is open.
# Registered at the ZMLK branch: proves per-branch number series run independently.
$visit3 = Step 'third visit at ZMLK starts its own series (P03.2)' {
    $v = Invoke-RestMethod -Method Post -Uri "$api/visits" -Headers $ha -ContentType 'application/json' -Body (@{
        patientId = $patient; branchId = $zamalek.id; testIds = @($gluF)
        isStat = $true; statReason = 'ICU follow-up' } | ConvertTo-Json)
    if ($v.visitNumber -notmatch '^V-ZMLK-\d{6}-0001$') { throw "expected V-ZMLK-…-0001, got $($v.visitNumber)" }
    $v
}
$v3sample = $visit3.samples[0].sampleId
Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit3.visitId)/samples/$v3sample/collect" -Headers $ha | Out-Null
Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit3.visitId)/samples/$v3sample/receive" -Headers $ha | Out-Null
$v3line = (Invoke-RestMethod -Uri "$api/visits/$($visit3.visitId)" -Headers $ha).tests[0].id
$v3result = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit3.visitId)/results" -Headers $ha `
    -ContentType 'application/json' -Body (@{ visitTestId = $v3line; value = 30 } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$api/results/$($v3result.resultId)/accept-technical" -Headers $ha | Out-Null
Invoke-RestMethod -Method Post -Uri "$api/results/$($v3result.resultId)/validate-medical" -Headers $hd `
    -ContentType 'application/json' -Body '{"interpretiveComment":null,"signatureIntent":"I medically validate GLU-F = 30 mg/dL"}' | Out-Null

ExpectError 'FINAL blocked while a critical value is open (P09.4 gate)' 422 {
    Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit3.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Final"}'
}
Step 'closing the critical unblocks the FINAL report' {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($v3result.resultId)/critical/document-call" -Headers $ha `
        -ContentType 'application/json' -Body '{"calledPerson":"Dr. Aya Salem","phone":"+201118887777","readBackConfirmed":true}' | Out-Null
    $r = Invoke-RestMethod -Method Post -Uri "$api/visits/$($visit3.visitId)/reports" -Headers $ha `
        -ContentType 'application/json' -Body '{"kind":"Final"}'
    if ($r.kind -ne 'Final') { throw 'final expected' }
} | Out-Null

Step 'reporting worklist shows the reported visits' {
    $wl = Invoke-RestMethod -Uri "$api/reports/worklist" -Headers $ha
    if (-not ($wl | Where-Object { $_.visitId -eq $visit2.visitId -and $_.kind -eq 'Final' })) { throw 'visit2 final missing from worklist' }
} | Out-Null

# ---------- M23: Executive dashboard ----------
Step 'dashboard KPIs reconcile with the day''s activity (P23.1)' {
    $d = Invoke-RestMethod -Uri "$api/analytics/dashboard" -Headers $ha
    if ($d.visitsToday -ne 3) { throw "expected 3 visits today, got $($d.visitsToday)" }
    if ($d.reportedToday -ne 2) { throw "expected 2 reported, got $($d.reportedToday)" }
    if ($d.reservedSamplesPending -ne 1) { throw "expected 1 reserved sample, got $($d.reservedSamplesPending)" }
    if ($d.openCriticalValues -ne 0) { throw "expected 0 open criticals, got $($d.openCriticalValues)" }
    if ($d.revenueToday -ne 380) { throw "expected revenue 380, got $($d.revenueToday)" }
    if ($null -eq $d.medianRegisterToReportMinutes) { throw 'expected a median TAT' }
    if (($d.pipeline | Where-Object stage -eq 'Reported').count -ne 2) { throw 'pipeline Reported mismatch' }
} | Out-Null

# ---------- Outbox dispatcher: reliable events -> metering & notifications ----------
Step 'outbox drains (at-least-once dispatch)' {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 2
        $s = Invoke-RestMethod -Uri "$api/platform/outbox/status" -Headers $ph
    } while ($s.pending -gt 0 -and (Get-Date) -lt $deadline)
    if ($s.pending -ne 0) { throw "outbox not drained: $($s.pending) pending" }
    if ($s.poisoned -ne 0) { throw "poisoned messages: $($s | ConvertTo-Json -Compress)" }
    if ($s.processed -lt 20) { throw "expected >=20 processed, got $($s.processed)" }
} | Out-Null

Step 'metering counted 2 finalized reports for tenant A (FR-SYS-011)' {
    $usage = Invoke-RestMethod -Uri "$api/platform/tenants/$tenantA/usage" -Headers $ph
    $month = $usage | Select-Object -First 1
    if ($month.finalizedReports -ne 2) { throw "expected 2 finalized reports, got $($month.finalizedReports)" }
} | Out-Null

# ---------- M02: Real users, login & role-based permissions ----------
Step 'initial Tenant Admin created via outbox; real login works' {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Seconds 2
        try {
            $script:adminAuth = Invoke-RestMethod -Method Post -Uri "$api/auth/login" -ContentType 'application/json' -Body (@{
                tenantId = $tenantA; userName = 'sara.hassan'; password = 'NileLab#Dev2026!' } | ConvertTo-Json)
        } catch { $script:adminAuth = $null }
    } while (-not $script:adminAuth -and (Get-Date) -lt $deadline)
    if (-not $adminAuth) { throw 'admin login failed (outbox consumer did not create the user?)' }
    if ($adminAuth.roles -notcontains 'TenantAdmin') { throw 'expected TenantAdmin role' }
} | Out-Null
$hAdmin = @{ Authorization = "Bearer $($adminAuth.token)" }

ExpectError 'wrong password rejected (indistinguishable failure)' 403 {
    Invoke-RestMethod -Method Post -Uri "$api/auth/login" -ContentType 'application/json' -Body (@{
        tenantId = $tenantA; userName = 'sara.hassan'; password = 'wrong-password-123' } | ConvertTo-Json)
}

Step 'admin creates a Technologist user (P02.1)' {
    Invoke-RestMethod -Method Post -Uri "$api/users" -Headers $hAdmin -ContentType 'application/json' -Body (@{
        userName = 'mostafa.kamal'; fullName = 'Mostafa Kamal'; password = 'Technolog#2026!x'
        roles = @('Technologist') } | ConvertTo-Json)
    $list = Invoke-RestMethod -Uri "$api/users" -Headers $hAdmin
    if ($list.Count -lt 2) { throw "expected >=2 users, got $($list.Count)" }
} | Out-Null

$techAuth = Invoke-RestMethod -Method Post -Uri "$api/auth/login" -ContentType 'application/json' -Body (@{
    tenantId = $tenantA; userName = 'mostafa.kamal'; password = 'Technolog#2026!x' } | ConvertTo-Json)
$hTech = @{ Authorization = "Bearer $($techAuth.token)" }

Step 'technologist can read visits with a real token' {
    Invoke-RestMethod -Uri "$api/visits/$($visit.visitId)" -Headers $hTech | Out-Null
} | Out-Null
ExpectError 'technologist cannot create users (role gate)' 403 {
    Invoke-RestMethod -Method Post -Uri "$api/users" -Headers $hTech -ContentType 'application/json' -Body (@{
        userName = 'rogue'; fullName = 'Rogue'; password = 'Whatever#2026!x'; roles = @('TenantAdmin') } | ConvertTo-Json)
}
ExpectError 'technologist cannot medically validate (role gate)' 403 {
    Invoke-RestMethod -Method Post -Uri "$api/results/$($gluResult.resultId)/validate-medical" -Headers $hTech `
        -ContentType 'application/json' -Body '{"interpretiveComment":null,"signatureIntent":"try"}'
}
ExpectError "tenant B admin cannot log into tenant A" 403 {
    Invoke-RestMethod -Method Post -Uri "$api/auth/login" -ContentType 'application/json' -Body (@{
        tenantId = $tenantA; userName = 'delta.admin'; password = 'DeltaLab#Dev2026!' } | ConvertTo-Json)
}

# ---------- FR-SYS-001: Audit trail & tamper evidence ----------
Step 'audit trail recorded the flow (who/what/when, before/after)' {
    $events = Invoke-RestMethod -Uri "$api/audit/events?take=500" -Headers $ha
    foreach ($required in @('Patient', 'Visit', 'TestResult', 'LabReport', 'Invoice')) {
        if (-not ($events | Where-Object { $_.entityType -eq $required })) { throw "no audit events for $required" }
    }
    $modified = $events | Where-Object { $_.action -eq 'Modified' -and $_.oldValues -and $_.newValues } | Select-Object -First 1
    if (-not $modified) { throw 'expected Modified events with before/after values' }
} | Out-Null

Step 'audit chain verifies intact' {
    $v = Invoke-RestMethod -Uri "$api/audit/verify-chain" -Headers $ha
    if (-not $v.valid -or $v.eventCount -lt 20) { throw "chain invalid or too few events: $($v | ConvertTo-Json -Compress)" }
} | Out-Null

Step 'TAMPER TEST: superuser edits history -> chain detects it' {
    & $psql -U postgres -h localhost -p $pgPort -d skylis -q -c `
        "UPDATE audit.audit_events SET new_values = replace(new_values, 'Mona', 'Someone Else') WHERE tenant_id = '$tenantA' AND entity_type = 'Patient' AND action = 'Created';" | Out-Null
    $v = Invoke-RestMethod -Uri "$api/audit/verify-chain" -Headers $ha
    if ($v.valid) { throw 'TAMPER NOT DETECTED — hash chain failed' }
    if (-not $v.firstBrokenEventId) { throw 'expected the broken event to be identified' }
} | Out-Null

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
