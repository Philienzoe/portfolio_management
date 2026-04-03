param(
    [string]$ApiBaseUrl = "http://localhost:5190",
    [string]$AdminEmail = "alice@example.com",
    [string]$AdminPassword = "Alice123!",
    [string]$DemoPassword = "Demo123!",
    [string]$DemoEmailPrefix = "is5413.demo",
    [string]$DemoDomain = "example.com",
    [int]$AccountCount = 100,
    [string]$DbServer = "localhost\SQLEXPRESS",
    [string]$Database = "IPMS",
    [switch]$ResetExistingDemoAccounts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers,
        [object]$Body
    )

    $invokeParams = @{
        Method      = $Method
        Uri         = $Uri
        ErrorAction = "Stop"
    }

    if ($Headers) {
        $invokeParams.Headers = $Headers
    }

    if ($null -ne $Body) {
        $invokeParams.ContentType = "application/json"
        $invokeParams.Body = $Body | ConvertTo-Json -Depth 10
    }

    try {
        return Invoke-RestMethod @invokeParams
    }
    catch {
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            throw "Request failed: $Method $Uri :: $responseBody"
        }

        throw
    }
}

function Get-ApiToken {
    param(
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$Password,
        [string]$FirstName,
        [string]$LastName
    )

    $loginBody = @{
        email    = $Email
        password = $Password
    }

    try {
        return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/login" -Body $loginBody
    }
    catch {
        if ([string]::IsNullOrWhiteSpace($FirstName) -or [string]::IsNullOrWhiteSpace($LastName)) {
            throw
        }

        $registerBody = @{
            email     = $Email
            password  = $Password
            firstName = $FirstName
            lastName  = $LastName
        }

        try {
            return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/register" -Body $registerBody
        }
        catch {
            return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/login" -Body $loginBody
        }
    }
}

function Reset-DemoAccounts {
    param([string]$NormalizedPrefix)

    $sql = @"
USE [$Database];
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DELETE FROM USERS
WHERE LOWER(email) LIKE 'diverse.demo%@example.com'
   OR LOWER(email) LIKE '$NormalizedPrefix%@example.com';
"@

    & sqlcmd -S $DbServer -E -C -Q $sql | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to reset existing demo accounts."
    }
}

function Get-InstrumentMap {
    $instruments = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/instruments"
    $map = @{}
    foreach ($instrument in $instruments) {
        $map[$instrument.tickerSymbol.ToUpperInvariant()] = $instrument
    }

    return $map
}

function Ensure-Instrument {
    param(
        [Parameter(Mandatory = $true)][string]$TickerSymbol,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][hashtable]$InstrumentMap
    )

    $normalized = $TickerSymbol.Trim().ToUpperInvariant()
    if ($InstrumentMap.ContainsKey($normalized)) {
        return $InstrumentMap[$normalized]
    }

    $body = @{
        tickerSymbol    = $normalized
        createIfMissing = $true
        range           = "6mo"
        interval        = "1d"
    }

    $result = Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/market-data/import/by-ticker" -Headers $Headers -Body $body
    $InstrumentMap[$result.tickerSymbol.ToUpperInvariant()] = $result
    Start-Sleep -Milliseconds 100
    return $result
}

function Get-OrCreatePortfolio {
    param(
        [Parameter(Mandatory = $true)][int]$UserId,
        [Parameter(Mandatory = $true)][string]$PortfolioName,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][hashtable]$Headers
    )

    $portfolios = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/users/$UserId/portfolios" -Headers $Headers
    $existing = @($portfolios) | Where-Object { $_.portfolioName -eq $PortfolioName } | Select-Object -First 1
    if ($existing) {
        return $existing
    }

    $body = @{
        portfolioName = $PortfolioName
        description   = $Description
        currency      = "USD"
    }

    return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/users/$UserId/portfolios" -Headers $Headers -Body $body
}

function Get-AvailablePool {
    param(
        [Parameter(Mandatory = $true)][string[]]$Tickers,
        [Parameter(Mandatory = $true)][hashtable]$InstrumentMap
    )

    return @(
        $Tickers |
            Where-Object { $InstrumentMap.ContainsKey($_.ToUpperInvariant()) } |
            Select-Object -Unique
    )
}

function Add-TickersFromPool {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$SelectedTickers,
        [Parameter(Mandatory = $true)][string[]]$Pool,
        [Parameter(Mandatory = $true)][int]$StartIndex,
        [Parameter(Mandatory = $true)][int]$CountToAdd
    )

    if ($Pool.Count -eq 0 -or $CountToAdd -le 0) {
        return
    }

    $attempts = 0
    $cursor = $StartIndex

    while ($attempts -lt ($Pool.Count * 4) -and $CountToAdd -gt 0) {
        $ticker = $Pool[$cursor % $Pool.Count]
        if (-not $SelectedTickers.Contains($ticker)) {
            $SelectedTickers.Add($ticker)
            $CountToAdd--
        }

        $cursor++
        $attempts++
    }
}

function New-AccountDefinition {
    param(
        [Parameter(Mandatory = $true)][int]$AccountNumber,
        [Parameter(Mandatory = $true)][string]$NormalizedPrefix,
        [Parameter(Mandatory = $true)][string]$Domain,
        [Parameter(Mandatory = $true)][string[]]$UsTechPool,
        [Parameter(Mandatory = $true)][string[]]$UsQualityPool,
        [Parameter(Mandatory = $true)][string[]]$UsFinancePool,
        [Parameter(Mandatory = $true)][string[]]$UsInnovationPool,
        [Parameter(Mandatory = $true)][string[]]$HkInternetPool,
        [Parameter(Mandatory = $true)][string[]]$HkBlueChipPool,
        [Parameter(Mandatory = $true)][string[]]$HkGrowthPool
    )

    $firstNames = @(
        "Olivia", "Daniel", "Sophia", "Marcus", "Grace", "Ryan", "Emily", "Kevin", "Isabella", "Jason",
        "Chloe", "Ethan", "Hannah", "Nathan", "Mia", "Lucas", "Ava", "Noah", "Zoe", "Liam"
    )
    $lastNames = @(
        "Chen", "Wong", "Lee", "Lau", "Chan", "Ho", "Ng", "Tsui", "Lam", "Yip",
        "Cheung", "Lo", "Tang", "Mak", "Poon", "Kwok", "Fung", "Chow", "Leung", "Yuen"
    )
    $portfolioThemes = @(
        "Pacific Tech Blend", "Global Blue Chips", "Consumer Mobility", "Income Stability", "AI Chipmakers",
        "Healthcare Finance", "Platform Leaders", "Core Compounders", "Innovation Asia", "Balanced Global",
        "Dividend Quality", "Industrial Momentum"
    )
    $portfolioValues = @(
        [decimal]280000, [decimal]320000, [decimal]360000, [decimal]410000, [decimal]470000,
        [decimal]540000, [decimal]620000, [decimal]720000, [decimal]850000, [decimal]980000
    )

    $selected = [System.Collections.Generic.List[string]]::new()
    $themeIndex = ($AccountNumber - 1) % $portfolioThemes.Count

    Add-TickersFromPool -SelectedTickers $selected -Pool $UsTechPool -StartIndex ($AccountNumber + $themeIndex) -CountToAdd 2
    Add-TickersFromPool -SelectedTickers $selected -Pool $UsQualityPool -StartIndex ($AccountNumber * 2 + $themeIndex) -CountToAdd 1
    Add-TickersFromPool -SelectedTickers $selected -Pool $UsFinancePool -StartIndex ($AccountNumber + 3 + $themeIndex) -CountToAdd 1

    if (($AccountNumber % 3) -eq 0) {
        Add-TickersFromPool -SelectedTickers $selected -Pool $UsInnovationPool -StartIndex ($AccountNumber + 5) -CountToAdd 1
    }

    Add-TickersFromPool -SelectedTickers $selected -Pool $HkInternetPool -StartIndex ($AccountNumber + $themeIndex) -CountToAdd 1
    Add-TickersFromPool -SelectedTickers $selected -Pool $HkBlueChipPool -StartIndex ($AccountNumber * 2 + $themeIndex) -CountToAdd 1
    Add-TickersFromPool -SelectedTickers $selected -Pool $HkGrowthPool -StartIndex ($AccountNumber * 3 + $themeIndex) -CountToAdd 1

    $allFallback = @($UsInnovationPool + $UsTechPool + $UsQualityPool + $UsFinancePool + $HkInternetPool + $HkBlueChipPool + $HkGrowthPool)
    Add-TickersFromPool -SelectedTickers $selected -Pool $allFallback -StartIndex ($AccountNumber + 7) -CountToAdd (7 - $selected.Count)

    return [pscustomobject]@{
        AccountNumber = $AccountNumber
        Email = ("{0}{1}@{2}" -f $NormalizedPrefix, $AccountNumber.ToString("00"), $Domain)
        FirstName = $firstNames[($AccountNumber - 1) % $firstNames.Count]
        LastName = $lastNames[(($AccountNumber - 1) * 3) % $lastNames.Count]
        PortfolioName = "{0} {1}" -f $portfolioThemes[$themeIndex], $AccountNumber.ToString("00")
        DemoValue = $portfolioValues[($AccountNumber - 1) % $portfolioValues.Count]
        Holdings = @($selected | Select-Object -First 7)
    }
}

$health = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/health"
if ($health.status -ne "Healthy") {
    throw "The API at $ApiBaseUrl is not healthy."
}

$normalizedPrefix = $DemoEmailPrefix.Trim().ToLowerInvariant()
if ($ResetExistingDemoAccounts.IsPresent) {
    Reset-DemoAccounts -NormalizedPrefix $normalizedPrefix
}

$adminAuth = Get-ApiToken -Email $AdminEmail -Password $AdminPassword
$adminHeaders = @{ Authorization = "$($adminAuth.tokenType) $($adminAuth.accessToken)" }
$instrumentMap = Get-InstrumentMap

$requiredTickers = @(
    "AAPL", "MSFT", "NVDA", "AMZN", "GOOGL", "BRK-B", "WM", "VST", "LLY", "BAC",
    "WFC", "AMAT", "TSLA", "TSM", "ASML", "CNI", "EPD", "NTRA", "V", "MA",
    "JPM", "META", "ORCL", "AVGO", "COST", "PG", "KO", "XOM", "ABBV", "PEP",
    "MCD", "HD", "ADBE", "CRM", "QCOM", "AMD", "NFLX", "LIN", "NEE", "CAT",
    "RTX", "HON", "SHOP", "INTU", "SPY", "QQQ",
    "0001.HK", "0005.HK", "0011.HK", "0016.HK", "0388.HK", "0700.HK", "0823.HK",
    "0857.HK", "0883.HK", "0941.HK", "1038.HK", "1093.HK", "1109.HK", "1211.HK",
    "1299.HK", "1810.HK", "2015.HK", "2269.HK", "2318.HK", "3690.HK", "9618.HK",
    "9888.HK", "9988.HK", "9999.HK"
)

$failedImports = [System.Collections.Generic.List[string]]::new()
foreach ($ticker in $requiredTickers) {
    try {
        Ensure-Instrument -TickerSymbol $ticker -Headers $adminHeaders -InstrumentMap $instrumentMap | Out-Null
    }
    catch {
        if (-not $failedImports.Contains($ticker)) {
            $failedImports.Add($ticker)
        }
    }
}

$instrumentMap = Get-InstrumentMap

$usTechPool = Get-AvailablePool -Tickers @("AAPL", "MSFT", "NVDA", "AMZN", "GOOGL", "META", "ORCL", "NFLX", "ADBE", "CRM") -InstrumentMap $instrumentMap
$usQualityPool = Get-AvailablePool -Tickers @("BRK-B", "WM", "COST", "PG", "KO", "MCD", "PEP", "HD", "LIN", "CAT") -InstrumentMap $instrumentMap
$usFinancePool = Get-AvailablePool -Tickers @("BAC", "WFC", "JPM", "V", "MA", "CNI", "ABBV", "RTX", "HON", "SPY", "QQQ") -InstrumentMap $instrumentMap
$usInnovationPool = Get-AvailablePool -Tickers @("AMAT", "TSLA", "TSM", "ASML", "LLY", "NTRA", "AVGO", "XOM", "AMD", "QCOM", "INTU", "NEE", "SHOP", "VST", "EPD") -InstrumentMap $instrumentMap

$hkInternetPool = Get-AvailablePool -Tickers @("0700.HK", "9988.HK", "3690.HK", "1810.HK", "9618.HK", "9888.HK", "9999.HK") -InstrumentMap $instrumentMap
$hkBlueChipPool = Get-AvailablePool -Tickers @("0005.HK", "0388.HK", "0941.HK", "1299.HK", "2318.HK", "0011.HK", "0016.HK", "0823.HK") -InstrumentMap $instrumentMap
$hkGrowthPool = Get-AvailablePool -Tickers @("1211.HK", "2015.HK", "2269.HK", "1093.HK", "1109.HK", "0883.HK", "0857.HK", "1038.HK", "0001.HK") -InstrumentMap $instrumentMap

if ($usTechPool.Count -lt 4 -or $usQualityPool.Count -lt 4 -or $usFinancePool.Count -lt 4 -or $usInnovationPool.Count -lt 4) {
    throw "Not enough US instruments were available to generate diverse demo accounts."
}

if ($hkInternetPool.Count -lt 3 -or $hkBlueChipPool.Count -lt 3 -or $hkGrowthPool.Count -lt 3) {
    throw "Not enough HK instruments were available to generate diverse demo accounts."
}

$weights = @([decimal]0.17, [decimal]0.15, [decimal]0.14, [decimal]0.13, [decimal]0.15, [decimal]0.14, [decimal]0.12)
$portfoliosHandled = 0
$transactionsCreated = 0
$demoEmails = [System.Collections.Generic.List[string]]::new()

for ($accountNumber = 1; $accountNumber -le $AccountCount; $accountNumber++) {
    $definition = New-AccountDefinition `
        -AccountNumber $accountNumber `
        -NormalizedPrefix $normalizedPrefix `
        -Domain $DemoDomain `
        -UsTechPool $usTechPool `
        -UsQualityPool $usQualityPool `
        -UsFinancePool $usFinancePool `
        -UsInnovationPool $usInnovationPool `
        -HkInternetPool $hkInternetPool `
        -HkBlueChipPool $hkBlueChipPool `
        -HkGrowthPool $hkGrowthPool

    $userAuth = Get-ApiToken `
        -Email $definition.Email `
        -Password $DemoPassword `
        -FirstName $definition.FirstName `
        -LastName $definition.LastName

    $userHeaders = @{ Authorization = "$($userAuth.tokenType) $($userAuth.accessToken)" }
    if (-not $demoEmails.Contains($definition.Email)) {
        $demoEmails.Add($definition.Email)
    }

    $description = "Demo portfolio with diversified US and Hong Kong stock holdings. Created on $(Get-Date -Format 'yyyy-MM-dd'). Includes 4 US positions and 3 HK positions for classroom demo use."
    $portfolio = Get-OrCreatePortfolio `
        -UserId ([int]$userAuth.user.userId) `
        -PortfolioName $definition.PortfolioName `
        -Description $description `
        -Headers $userHeaders

    $portfoliosHandled++

    $existingHoldings = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/portfolios/$($portfolio.portfolioId)/holdings" -Headers $userHeaders
    $existingTickers = @($existingHoldings) | ForEach-Object { $_.tickerSymbol.ToUpperInvariant() }

    for ($holdingIndex = 0; $holdingIndex -lt $definition.Holdings.Count; $holdingIndex++) {
        $ticker = $definition.Holdings[$holdingIndex]
        $instrument = $instrumentMap[$ticker.ToUpperInvariant()]
        if (-not $instrument) {
            continue
        }

        if ($existingTickers -contains $instrument.tickerSymbol.ToUpperInvariant()) {
            continue
        }

        if ($null -eq $instrument.currentPrice -or [decimal]$instrument.currentPrice -le 0) {
            continue
        }

        $weight = $weights[$holdingIndex]
        $targetDollarValue = [decimal]$definition.DemoValue * $weight
        $quantity = [decimal]::Round($targetDollarValue / [decimal]$instrument.currentPrice, 8)
        if ($quantity -le 0) {
            continue
        }

        $priceFactor = switch ($holdingIndex) {
            0 { [decimal]0.93; break }
            1 { [decimal]0.91; break }
            2 { [decimal]0.94; break }
            3 { [decimal]0.92; break }
            4 { [decimal]0.90; break }
            5 { [decimal]0.89; break }
            default { [decimal]0.88; break }
        }

        $pricePerUnit = [decimal]::Round(([decimal]$instrument.currentPrice * $priceFactor), 4)
        $transactionDate = (Get-Date).ToUniversalTime().AddDays(-1 * (12 + ($accountNumber % 17) + ($holdingIndex * 4)))
        $body = @{
            instrumentId    = [int]$instrument.instrumentId
            transactionType = "Buy"
            quantity        = $quantity
            pricePerUnit    = $pricePerUnit
            transactionDate = $transactionDate.ToString("o")
            fees            = [decimal]0
            notes           = "IS5413 demo holding loaded on $(Get-Date -Format 'yyyy-MM-dd')."
        }

        Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/portfolios/$($portfolio.portfolioId)/transactions" -Headers $userHeaders -Body $body | Out-Null
        $transactionsCreated++
        Start-Sleep -Milliseconds 40
    }
}

$summary = [pscustomobject]@{
    importedAtUtc       = (Get-Date).ToUniversalTime().ToString("o")
    emailPrefix         = $normalizedPrefix
    demoPassword        = $DemoPassword
    accountsCreated     = $AccountCount
    portfoliosHandled   = $portfoliosHandled
    transactionsCreated = $transactionsCreated
    failedImports       = @($failedImports)
    sampleDemoEmails    = @($demoEmails | Select-Object -First 10)
}

$summary | ConvertTo-Json -Depth 6
