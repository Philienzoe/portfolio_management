param(
    [string]$ApiBaseUrl = "http://localhost:5190",
    [string]$AdminEmail = "alice@example.com",
    [string]$AdminPassword = "Alice123!",
    [string]$DbServer = "localhost\SQLEXPRESS",
    [string]$Database = "IPMS"
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
    $body = @{
        email    = $AdminEmail
        password = $AdminPassword
    }

    return Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/auth/login" -Body $body
}

function Resolve-AccessToken {
    param([Parameter(Mandatory = $true)]$AuthResponse)

    if ($AuthResponse.PSObject.Properties.Name -contains 'accessToken') {
        return [string]$AuthResponse.accessToken
    }

    if ($AuthResponse.PSObject.Properties.Name -contains 'token') {
        return [string]$AuthResponse.token
    }

    throw "The authentication response did not contain an access token."
}

function Get-OpenSqlConnection {
    $connectionString = "Server=$DbServer;Database=$Database;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;"
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    return $connection
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$CommandText,
        [hashtable]$Parameters
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $CommandText

    if ($Parameters) {
        foreach ($key in $Parameters.Keys) {
            $parameter = $command.Parameters.Add("@$key", [System.Data.SqlDbType]::NVarChar, 4000)
            $parameter.Value = if ($null -eq $Parameters[$key]) { [DBNull]::Value } else { [string]$Parameters[$key] }
        }
    }

    try {
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Invoke-SqlNonQuery {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$CommandText,
        [System.Collections.IDictionary]$Parameters
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $CommandText

    if ($Parameters) {
        foreach ($key in $Parameters.Keys) {
            $value = $Parameters[$key]
            $parameter = $command.Parameters.AddWithValue("@$key", $value)
            if ($null -eq $value) {
                $parameter.Value = [DBNull]::Value
            }
        }
    }

    try {
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $command.Dispose()
    }
}

function Get-ExchangeIds {
    param([Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection)

    return @{
        XNAS = [int](Invoke-SqlScalar -Connection $Connection -CommandText "SELECT exchange_id FROM STOCK_EXCHANGES WHERE mic_code = 'XNAS';")
        XNYS = [int](Invoke-SqlScalar -Connection $Connection -CommandText "SELECT exchange_id FROM STOCK_EXCHANGES WHERE mic_code = 'XNYS';")
        XHKG = [int](Invoke-SqlScalar -Connection $Connection -CommandText "SELECT exchange_id FROM STOCK_EXCHANGES WHERE mic_code = 'XHKG';")
    }
}

function Ensure-PlaceholderInstrument {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$TickerSymbol,
        [Parameter(Mandatory = $true)][string]$InstrumentType,
        [Parameter(Mandatory = $true)][string]$Name,
        [Nullable[int]]$ExchangeId
    )

    $existingId = Invoke-SqlScalar `
        -Connection $Connection `
        -CommandText "SELECT instrument_id FROM FINANCIAL_INSTRUMENTS WHERE ticker_symbol = @tickerSymbol;" `
        -Parameters @{ tickerSymbol = $TickerSymbol }

    if ($existingId) {
        return [pscustomobject]@{
            Created      = $false
            InstrumentId = [int]$existingId
        }
    }

    $instrumentId = Invoke-SqlScalar `
        -Connection $Connection `
        -CommandText @"
INSERT INTO FINANCIAL_INSTRUMENTS (ticker_symbol, instrument_type, name, current_price, last_updated)
OUTPUT INSERTED.instrument_id
VALUES (@tickerSymbol, @instrumentType, @name, NULL, SYSDATETIME());
"@ `
        -Parameters @{
            tickerSymbol   = $TickerSymbol
            instrumentType = $InstrumentType
            name           = $Name
        }

    if ($InstrumentType -eq "STOCK") {
        Invoke-SqlNonQuery `
            -Connection $Connection `
            -CommandText @"
INSERT INTO STOCKS (instrument_id, exchange_id)
VALUES (@instrumentId, @exchangeId);
"@ `
            -Parameters @{
                instrumentId = [int]$instrumentId
                exchangeId   = $ExchangeId
            }
    }
    elseif ($InstrumentType -eq "CRYPTO") {
        Invoke-SqlNonQuery `
            -Connection $Connection `
            -CommandText @"
INSERT INTO CRYPTOCURRENCIES (instrument_id)
VALUES (@instrumentId);
"@ `
            -Parameters @{
                instrumentId = [int]$instrumentId
            }
    }

    return [pscustomobject]@{
        Created      = $true
        InstrumentId = [int]$instrumentId
    }
}

function Get-Top100HongKongStocks {
    $items = New-Object System.Collections.Generic.List[object]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $urls = @(
        "https://companiesmarketcap.com/hong-kong/largest-companies-in-hong-kong-by-market-cap/",
        "https://companiesmarketcap.com/hong-kong/largest-companies-in-hong-kong-by-market-cap/?page=2"
    )

    foreach ($url in $urls) {
        $response = Invoke-WebRequest -UseBasicParsing $url
        $html = $response.Content
        $matches = [regex]::Matches(
            $html,
            '<div class="company-name">(?<name>.*?)</div><div class="company-code"><span class="rank d-none"></span>(?<ticker>\d{4}\.HK)</div>',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)

        foreach ($match in $matches) {
            $ticker = $match.Groups['ticker'].Value.Trim().ToUpperInvariant()
            $name = [System.Net.WebUtility]::HtmlDecode(($match.Groups['name'].Value -replace '<.*?>', '').Trim())

            if ($seen.Add($ticker)) {
                $items.Add([pscustomobject]@{
                    TickerSymbol = $ticker
                    Name         = $name
                    ExchangeCode = "XHKG"
                    Group        = "HK_TOP_100"
                })
            }

            if ($items.Count -ge 100) {
                return $items
            }
        }
    }

    return $items
}

function Get-Sp500Stocks {
    $response = Invoke-WebRequest -UseBasicParsing "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies"
    $html = $response.Content
    $tableMatch = [regex]::Match(
        $html,
        '<table class="wikitable sortable[^"]*" id="constituents">(?<table>.*?)</table>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if (-not $tableMatch.Success) {
        throw "Unable to locate the S&P 500 constituents table."
    }

    $rowMatches = [regex]::Matches(
        $tableMatch.Groups['table'].Value,
        '<tr>\s*<td><a[^>]*>(?<symbol>[^<]+)</a>\s*</td>\s*<td>(?<name>.*?)</td>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    $items = foreach ($match in $rowMatches) {
        $symbol = $match.Groups['symbol'].Value.Trim().ToUpperInvariant().Replace('.', '-')
        $name = [System.Net.WebUtility]::HtmlDecode(($match.Groups['name'].Value -replace '<.*?>', '').Trim())

        [pscustomobject]@{
            TickerSymbol = $symbol
            Name         = $name
            ExchangeCode = $null
            Group        = "SP500"
        }
    }

    return @($items)
}

function Get-CryptoSymbols {
    $symbols = @(
        "WEETH-USD",
        "XLM-USD",
        "DAI-USD",
        "CC37263-USD",
        "AETHUSDT-USD",
        "USD136148-USD",
        "BTCB-USD",
        "LTC-USD",
        "PYUSD-USD",
        "ZEC-USD",
        "AVAX-USD",
        "HBAR-USD",
        "M35491-USD",
        "RAIN38341-USD",
        "SUSDE-USD",
        "SHIB-USD",
        "SUI20947-USD",
        "TAO22974-USD",
        "TON11419-USD",
        "WLFI33251-USD",
        "CRO-USD",
        "XAUT-USD",
        "PAXG-USD",
        "BTC-USD",
        "ETH-USD",
        "USDT-USD",
        "XRP-USD",
        "BNB-USD",
        "USDC-USD",
        "SOL-USD",
        "TRX-USD",
        "WTRX-USD",
        "STETH-USD",
        "DOGE-USD",
        "USDS33039-USD",
        "LEO-USD",
        "WSTETH-USD",
        "HYPE32196-USD",
        "BCH-USD",
        "ADA-USD",
        "WBTC-USD",
        "WBETH-USD",
        "WETH-USD",
        "LINK-USD",
        "AETHWETH-USD",
        "USDE29470-USD",
        "XMR-USD",
        "CBBTC32994-USD"
    )

    return @(
        $symbols |
            Select-Object -Unique |
            ForEach-Object {
                [pscustomobject]@{
                    TickerSymbol = $_
                    Name         = ($_.Replace('-USD', ''))
                    ExchangeCode = $null
                    Group        = "CRYPTO_SCREENSHOT"
                }
            }
    )
}

function Import-Symbol {
    param(
        [Parameter(Mandatory = $true)][pscustomobject]$Item,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][hashtable]$ExchangeIds
    )

    $body = @{
        tickerSymbol    = $Item.TickerSymbol
        createIfMissing = $true
        range           = "1mo"
        interval        = "1d"
    }

    try {
        $result = Invoke-JsonRequest -Method Post -Uri "$ApiBaseUrl/api/market-data/import/by-ticker" -Headers $Headers -Body $body
        return [pscustomobject]@{
            TickerSymbol = $Item.TickerSymbol
            Group        = $Item.Group
            Status       = "IMPORTED"
            Created      = [bool]$result.createdInstrument
            Name         = $result.name
            Message      = $null
        }
    }
    catch {
        $instrumentType = if ($Item.Group -eq "CRYPTO_SCREENSHOT") { "CRYPTO" } else { "STOCK" }
        $exchangeId = if ($Item.ExchangeCode -and $ExchangeIds.ContainsKey($Item.ExchangeCode)) { $ExchangeIds[$Item.ExchangeCode] } else { $null }
        $placeholder = Ensure-PlaceholderInstrument `
            -Connection $Connection `
            -TickerSymbol $Item.TickerSymbol `
            -InstrumentType $instrumentType `
            -Name $Item.Name `
            -ExchangeId $exchangeId

        return [pscustomobject]@{
            TickerSymbol = $Item.TickerSymbol
            Group        = $Item.Group
            Status       = if ($placeholder.Created) { "PLACEHOLDER_CREATED" } else { "PLACEHOLDER_EXISTS" }
            Created      = [bool]$placeholder.Created
            Name         = $Item.Name
            Message      = $_.Exception.Message
        }
    }
}

$token = Get-ApiToken
$headers = @{ Authorization = "Bearer $(Resolve-AccessToken -AuthResponse $token)" }
$connection = $null
$connection = Get-OpenSqlConnection

try {
    $exchangeIds = Get-ExchangeIds -Connection $connection

    $allItems = @()
    $allItems += Get-CryptoSymbols
    $allItems += Get-Top100HongKongStocks
    $allItems += Get-Sp500Stocks

    $results = New-Object System.Collections.Generic.List[object]
    $total = $allItems.Count
    $counter = 0

    foreach ($item in $allItems) {
        $counter++
        Write-Host ("[{0}/{1}] {2} ({3})" -f $counter, $total, $item.TickerSymbol, $item.Group)
        $results.Add((Import-Symbol -Item $item -Headers $headers -Connection $connection -ExchangeIds $exchangeIds))
        Start-Sleep -Milliseconds 250
    }

    $summary = $results |
        Group-Object Group |
        ForEach-Object {
            $groupItems = $_.Group
            [pscustomobject]@{
                Group               = $_.Name
                Requested           = $groupItems.Count
                Imported            = @($groupItems | Where-Object { $_.Status -eq "IMPORTED" }).Count
                PlaceholderCreated  = @($groupItems | Where-Object { $_.Status -eq "PLACEHOLDER_CREATED" }).Count
                PlaceholderExisting = @($groupItems | Where-Object { $_.Status -eq "PLACEHOLDER_EXISTS" }).Count
            }
        }

    Write-Host ""
    Write-Host "Import summary:" -ForegroundColor Cyan
    $summary | Format-Table -AutoSize

    Write-Host ""
    Write-Host "Unsupported symbols that fell back to placeholders:" -ForegroundColor Yellow
    $results |
        Where-Object { $_.Status -ne "IMPORTED" } |
        Select-Object Group, TickerSymbol, Status, Message |
        Format-Table -AutoSize
}
finally {
    if ($null -ne $connection -and $connection.State -eq [System.Data.ConnectionState]::Open) {
        $connection.Close()
    }

    if ($null -ne $connection) {
        $connection.Dispose()
    }
}
