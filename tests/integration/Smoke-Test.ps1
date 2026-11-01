param(
    [string]$PipelineApi = "http://localhost:8003/api",
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"

function Assert-Equal($Actual, $Expected, $Message) {
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Submit-And-Wait([string]$Fixture, [string]$StorageTarget) {
    $filePath = (Resolve-Path $Fixture).Path
    $json = & curl.exe --fail-with-body --silent --show-error `
        -F "file=@$filePath" `
        -F "schemaId=payment-instruction-v1" `
        -F "storageTarget=$StorageTarget" `
        "$PipelineApi/import"
    if ($LASTEXITCODE -ne 0) {
        throw "Import upload failed for '$filePath': $json"
    }

    $accepted = $json | ConvertFrom-Json
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 2
        $run = Invoke-RestMethod "$PipelineApi/runs/$($accepted.runId)"
        if ($run.status -eq "failed") {
            throw "Import '$($run.inputPath)' failed: $($run.lastError)"
        }
        if ((Get-Date) -gt $deadline) {
            throw "Timed out waiting for import '$($run.inputPath)' (stage: $($run.stage))."
        }
    } while ($run.status -notin @("completed", "cancelled"))

    if ($run.status -ne "completed") {
        throw "Import '$($run.inputPath)' ended with status '$($run.status)'."
    }

    return $run
}

$dependencies = Invoke-RestMethod "$PipelineApi/dependencies"
$unhealthy = @($dependencies.services | Where-Object { -not $_.healthy })
if ($unhealthy.Count -gt 0) {
    $details = ($unhealthy | ForEach-Object { "$($_.name): $($_.detail)" }) -join "`n"
    throw "Not all local services are healthy:`n$details"
}

$schemas = Invoke-RestMethod "$PipelineApi/schemas"
if (-not ($schemas.schemas | Where-Object { $_.schemaId -eq "payment-instruction-v1" })) {
    throw "The built-in payment-instruction-v1 schema is missing."
}

$chroma = Invoke-RestMethod "$PipelineApi/storage/chroma/collections"
if ($chroma.collections.Count -eq 0) {
    throw "ChromaDB has no collections. Check ISO rule seeding."
}
$firstCollection = $chroma.collections[0]
$chromaDocuments = Invoke-RestMethod "$PipelineApi/storage/chroma/collections/$($firstCollection.id)/documents?limit=10&offset=0"
if ($chromaDocuments.documents.Count -eq 0) {
    throw "ChromaDB has no seeded ISO rule documents."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$cleanRun = Submit-And-Wait (Join-Path $root "FinGuardPlatform.PipelineApp\samples\generated\payments-clean.fw") "MongoDb"
Assert-Equal $cleanRun.cleanPublished 3 "Clean record count."
Assert-Equal $cleanRun.deadLettered 0 "Clean dead-letter count."
Assert-Equal $cleanRun.auditComments.Count 3 "Local AI audit comment count."

$badRun = Submit-And-Wait (Join-Path $root "FinGuardPlatform.PipelineApp\samples\generated\payments-malformed.fw") "DynamoDbLocal"
Assert-Equal $badRun.cleanPublished 0 "Malformed clean record count."
Assert-Equal $badRun.deadLettered 4 "Malformed dead-letter count."
Assert-Equal $badRun.deadLettersPersisted 4 "Persisted dead-letter count."
Assert-Equal $badRun.auditComments.Count 4 "Malformed-record local AI audit comment count."

$mongo = Invoke-RestMethod "$PipelineApi/storage/mongodb/collections/clean_records/documents?limit=10"
if ($mongo.documents.Count -lt 3) {
    throw "MongoDB browser did not find the clean sample records."
}

$tables = Invoke-RestMethod "$PipelineApi/storage/dynamodb/tables"
if (-not ($tables.tables -contains "finguard_clean_records_dlq")) {
    throw "DynamoDB Local dead-letter table was not created."
}
$dynamoItems = Invoke-RestMethod "$PipelineApi/storage/dynamodb/tables/finguard_clean_records_dlq/items?limit=10&offset=0"
if ($dynamoItems.rows.Count -lt 4) {
    throw "DynamoDB Local browser did not find the malformed sample records."
}

$duckdbHealth = Invoke-RestMethod "http://localhost:8002/api/health"
Assert-Equal $duckdbHealth.status "ok" "DuckDB API health."

Write-Host "Smoke test passed: local services, schema listing, Mongo clean records, DynamoDB DLQ records, ChromaDB, and DuckDB are reachable."
