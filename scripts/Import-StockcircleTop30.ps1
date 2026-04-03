param(
    [string]$ApiBaseUrl = "http://localhost:5190",
    [int]$TopCount = 30,
    [string]$DemoEmail = "stockcircle.demo@example.com",
    [string]$DemoPassword = "Demo123!",
    [string]$SourceUrl = "https://stockcircle.com/best-investors",
    [string]$DbServer = "localhost\SQLEXPRESS",
    [string]$Database = "IPMS",
    [switch]$ResetExistingDemoPortfolios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RequiredMatchValue {
    param(
        [Parameter(Mandatory = $true)][string]$InputText,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $match = [regex]::Match($InputText, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        throw "Unable to parse required pattern '$Pattern'."
    }

    return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value).Trim()
}

function Get-OptionalMatchValue {
    param(
        [Parameter(Mandatory = $true)][string]$InputText,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $match = [regex]::Match($InputText, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        return $null
    }

    return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value).Trim()
}

function Convert-PortfolioValueToUsd {
    param([string]$PortfolioValueText)

    $match = [regex]::Match($PortfolioValueText, '\$([0-9]+(?:\.[0-9]+)?)([kMBT]?)\s+portfolio')
    if (-not $match.Success) {
        return [decimal]0
    }

    $number = [decimal]::Parse($match.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
    $suffix = $match.Groups[2].Value

    $multiplier = switch ($suffix) {
        "k" { [decimal]1e3; break }
        "M" { [decimal]1e6; break }
        "B" { [decimal]1e9; break }
        "T" { [decimal]1e12; break }
        default { [decimal]1 }
    }

    return $number * $multiplier
}

function Get-DemoPortfolioValue {
    param([decimal]$ActualPortfolioValue)

    if ($ActualPortfolioValue -lt 100000000) {
        return [decimal]100000
    }

    if ($ActualPortfolioValue -lt 1000000000) {
        return [decimal]250000
    }

    if ($ActualPortfolioValue -lt 10000000000) {
        return [decimal]500000
    }

    if ($ActualPortfolioValue -lt 100000000000) {
        return [decimal]1000000
    }

    return [decimal]2000000
}

function Get-TickerCandidates {
    param([string]$TickerSymbol)

    $candidates = [System.Collections.Generic.List[string]]::new()
    $normalized = $TickerSymbol.Trim().ToUpperInvariant()

    $tickerAliases = @{
        "ANTM" = @("ELV")
        "CHK"  = @("EXE")
        "CNHI" = @("CNH")
        "SNE"  = @("SONY")
    }

    $candidates.Add($normalized)
    if ($tickerAliases.ContainsKey($normalized)) {
        foreach ($alias in $tickerAliases[$normalized]) {
            $candidates.Add($alias)
        }
    }

    if ($normalized.Contains(".") -and -not $normalized.EndsWith(".HK", [System.StringComparison]::OrdinalIgnoreCase)) {
        $candidates.Add($normalized.Replace(".", "-"))
    }

    if ($normalized.Contains("-")) {
        $candidates.Add($normalized.Replace("-", "."))
    }

    return $candidates | Select-Object -Unique
}

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
        $invokeParams.Body = $Body | ConvertTo-Json -Depth 8
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
        [Parameter(Mandatory = $true)][string]$Password
    )

    $loginBody = @{
        email    = $Email
        password = $Password
    }

    try {
        return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/login" -Body $loginBody
    }
    catch {
        $registerBody = @{
            email     = $Email
            password  = $Password
            firstName = "Stockcircle"
            lastName  = "Demo"
        }

        try {
            return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/register" -Body $registerBody
        }
        catch {
            return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/login" -Body $loginBody
        }
    }
}

function Get-StockcircleProfiles {
    param([int]$Limit)

    $html = (Invoke-WebRequest -UseBasicParsing $SourceUrl).Content
    $matches = [regex]::Matches(
        $html,
        '<a class="home-box" href="(?<href>/portfolio/[^"]+)">(?<body>.*?)</a>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    $profiles = [System.Collections.Generic.List[object]]::new()

    foreach ($match in $matches) {
        $body = $match.Groups["body"].Value
        $name = Get-RequiredMatchValue -InputText $body -Pattern '<h2 class="home-box__title">(.*?)</h2>'
        $firm = Get-RequiredMatchValue -InputText $body -Pattern '<h3 class="home-box__subtitle">(.*?)</h3>'
        $performance = Get-OptionalMatchValue -InputText $body -Pattern 'Performance:\s*([0-9\.-]+%)\s*last year'
        $portfolioValueText = Get-RequiredMatchValue -InputText $body -Pattern '<p class="home-box__portfolio-value">\s*(.*?)\s*</p>'
        $actualPortfolioValue = Convert-PortfolioValueToUsd -PortfolioValueText $portfolioValueText

        if ($actualPortfolioValue -le 0) {
            continue
        }

        $tickerMatches = [regex]::Matches($body, 'logos/symbol/(?<ticker>[^"<]+)')
        $tickerSymbols = $tickerMatches |
            ForEach-Object { [System.Net.WebUtility]::HtmlDecode($_.Groups["ticker"].Value).Trim().ToUpperInvariant() } |
            Select-Object -Unique |
            Select-Object -First 3

        if ($tickerSymbols.Count -eq 0) {
            continue
        }

        $profiles.Add([pscustomobject]@{
            Rank                 = $profiles.Count + 1
            Name                 = $name
            Firm                 = $firm
            Slug                 = $match.Groups["href"].Value.Substring("/portfolio/".Length)
            Performance          = $performance
            PortfolioValueText   = $portfolioValueText
            ActualPortfolioValue = $actualPortfolioValue
            DemoPortfolioValue   = Get-DemoPortfolioValue -ActualPortfolioValue $actualPortfolioValue
            TickerSymbols        = @($tickerSymbols)
        })

        if ($profiles.Count -ge $Limit) {
            break
        }
    }

    return $profiles
}

function Get-InstrumentMap {
    param([hashtable]$Headers)

    $instruments = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/instruments" -Headers $Headers
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

    foreach ($candidate in Get-TickerCandidates -TickerSymbol $TickerSymbol) {
        if ($InstrumentMap.ContainsKey($candidate)) {
            return $InstrumentMap[$candidate]
        }

        $body = @{
            tickerSymbol    = $candidate
            createIfMissing = $true
            range           = "6mo"
            interval        = "1d"
        }

        try {
            $result = Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/market-data/import/by-ticker" -Headers $Headers -Body $body
            $InstrumentMap[$result.tickerSymbol.ToUpperInvariant()] = $result
            Start-Sleep -Milliseconds 250
            return $result
        }
        catch {
            continue
        }
    }

    throw "Unable to import instrument '$TickerSymbol' from Yahoo Finance."
}

function Get-OrCreatePortfolio {
    param(
        [Parameter(Mandatory = $true)][int]$UserId,
        [Parameter(Mandatory = $true)][string]$PortfolioName,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][hashtable]$Headers
    )

    $userPortfolios = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/users/$UserId/portfolios" -Headers $Headers
    $existing = @($userPortfolios) | Where-Object { $_.portfolioName -eq $PortfolioName } | Select-Object -First 1
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

function Reset-DemoPortfolios {
    param([int]$UserId)

    $sql = @"
USE [$Database];
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DELETE t
FROM TRANSACTIONS t
INNER JOIN PORTFOLIOS p ON p.portfolio_id = t.portfolio_id
WHERE p.user_id = $UserId;

DELETE h
FROM PORTFOLIO_HOLDINGS h
INNER JOIN PORTFOLIOS p ON p.portfolio_id = h.portfolio_id
WHERE p.user_id = $UserId;

DELETE FROM PORTFOLIOS
WHERE user_id = $UserId;
"@

    & sqlcmd -S $DbServer -E -C -Q $sql | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to reset existing demo portfolios for user $UserId."
    }
}

$health = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/health"
if ($health.status -ne "Healthy") {
    throw "The API at $ApiBaseUrl is not healthy."
}

$auth = Get-ApiToken -Email $DemoEmail -Password $DemoPassword
$headers = @{ Authorization = "$($auth.tokenType) $($auth.accessToken)" }
$demoUserId = [int]$auth.user.userId

if ($ResetExistingDemoPortfolios.IsPresent) {
    Reset-DemoPortfolios -UserId $demoUserId
}

$profiles = Get-StockcircleProfiles -Limit $TopCount
if ($profiles.Count -lt $TopCount) {
    throw "Only found $($profiles.Count) Stockcircle profiles. Expected at least $TopCount."
}

$instrumentMap = Get-InstrumentMap -Headers $headers
$importedTickers = [System.Collections.Generic.List[string]]::new()
$missingTickers = [System.Collections.Generic.List[string]]::new()

foreach ($profile in $profiles) {
    foreach ($ticker in $profile.TickerSymbols) {
        try {
            $instrument = Ensure-Instrument -TickerSymbol $ticker -Headers $headers -InstrumentMap $instrumentMap
            if (-not $importedTickers.Contains($instrument.tickerSymbol)) {
                $importedTickers.Add($instrument.tickerSymbol)
            }
        }
        catch {
            if (-not $missingTickers.Contains($ticker)) {
                $missingTickers.Add($ticker)
            }
        }
    }
}

$instrumentMap = Get-InstrumentMap -Headers $headers
$weights = @([decimal]0.50, [decimal]0.30, [decimal]0.20)
$completedPortfolios = 0

foreach ($profile in $profiles) {
    $portfolioName = "$($profile.Name) Portfolio"
    $description = "Imported from Stockcircle best-investors on $(Get-Date -Format 'yyyy-MM-dd'). Firm: $($profile.Firm). Performance: $($profile.Performance). Source portfolio value: $($profile.PortfolioValueText). Holdings represent the top 3 displayed positions for demo use."
    $portfolio = Get-OrCreatePortfolio -UserId $demoUserId -PortfolioName $portfolioName -Description $description -Headers $headers

    $existingHoldings = Invoke-JsonRequest -Method Get -Uri "$ApiBaseUrl/api/portfolios/$($portfolio.portfolioId)/holdings" -Headers $headers
    $existingTickers = @($existingHoldings) | ForEach-Object { $_.tickerSymbol.ToUpperInvariant() }

    $positionIndex = 0
    foreach ($ticker in $profile.TickerSymbols) {
        $instrument = $null
        foreach ($candidate in Get-TickerCandidates -TickerSymbol $ticker) {
            if ($instrumentMap.ContainsKey($candidate)) {
                $instrument = $instrumentMap[$candidate]
                break
            }
        }

        if (-not $instrument) {
            continue
        }

        if ($existingTickers -contains $instrument.tickerSymbol.ToUpperInvariant()) {
            $positionIndex++
            continue
        }

        if ($null -eq $instrument.currentPrice -or [decimal]$instrument.currentPrice -le 0) {
            $positionIndex++
            continue
        }

        $weight = if ($positionIndex -lt $weights.Count) { $weights[$positionIndex] } else { [decimal]0.10 }
        $targetDollarValue = [decimal]$profile.DemoPortfolioValue * $weight
        $quantity = [decimal]::Round($targetDollarValue / [decimal]$instrument.currentPrice, 8)
        if ($quantity -le 0) {
            $positionIndex++
            continue
        }

        $priceFactor = switch ($positionIndex) {
            0 { [decimal]0.94; break }
            1 { [decimal]0.91; break }
            default { [decimal]0.88; break }
        }

        $pricePerUnit = [decimal]::Round(([decimal]$instrument.currentPrice * $priceFactor), 4)
        $transactionDate = (Get-Date).ToUniversalTime().AddDays(-1 * (45 + ($profile.Rank * 2) + ($positionIndex * 7)))
        $notes = "Imported from Stockcircle best-investors on $(Get-Date -Format 'yyyy-MM-dd') using the public top holdings shown for $($profile.Name)."

        $body = @{
            instrumentId    = [int]$instrument.instrumentId
            transactionType = "Buy"
            quantity        = $quantity
            pricePerUnit    = $pricePerUnit
            transactionDate = $transactionDate.ToString("o")
            fees            = [decimal]0
            notes           = $notes
        }

        Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/portfolios/$($portfolio.portfolioId)/transactions" -Headers $headers -Body $body | Out-Null
        Start-Sleep -Milliseconds 100
        $positionIndex++
    }

    $completedPortfolios++
}

$summary = [pscustomobject]@{
    importedAtUtc      = (Get-Date).ToUniversalTime().ToString("o")
    source             = $SourceUrl
    demoUserEmail      = $DemoEmail
    demoUserPassword   = $DemoPassword
    demoUserId         = $demoUserId
    profilesImported   = $profiles.Count
    uniqueTickers      = $importedTickers.Count
    missingTickers     = @($missingTickers)
    portfoliosHandled  = $completedPortfolios
}

$summary | ConvertTo-Json -Depth 6
