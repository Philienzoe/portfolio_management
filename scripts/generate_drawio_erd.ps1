param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'ipms-3nf-diagrams.xml')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-Line {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Text
    )

    $null = $Builder.AppendLine($Text)
}

function Escape-Xml {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Field-Width {
    param([string]$Name)

    $base = [Math]::Ceiling(($Name.Length * 6.0) + 26)
    return [int][Math]::Max(78, [Math]::Min(176, $base))
}

function New-FieldId {
    param(
        [string]$Prefix,
        [string]$TableId,
        [string]$FieldName
    )

    $safe = ($FieldName -replace '[^A-Za-z0-9_]', '_')
    return '{0}_{1}_{2}' -f $Prefix, $TableId, $safe
}

function New-LabelId {
    param(
        [string]$Prefix,
        [string]$TableId
    )

    return '{0}_{1}_label' -f $Prefix, $TableId
}

function New-TableGeometry {
    param(
        [hashtable]$Table
    )

    $fieldX = $Table.X
    $result = [ordered]@{
        Label = @{
            X = $Table.X
            Y = $Table.Y
            Width = 260
            Height = 20
        }
        Fields = [ordered]@{}
        TableWidth = 0
    }

    foreach ($field in $Table.Fields) {
        $width = Field-Width $field.Name
        $result.Fields[$field.Name] = @{
            X = $fieldX
            Y = $Table.Y + 22
            Width = $width
            Height = 42
            Kind = $field.Kind
        }
        $fieldX += $width
    }

    $result.TableWidth = $fieldX - $Table.X
    return $result
}

function Field-Style {
    param([string]$Kind)

    $fontColor = switch ($Kind) {
        'pk' { '#C0392B' }
        'fk' { '#27AE60' }
        'pfk' { '#8E44AD' }
        default { '#111111' }
    }

    return "shape=rectangle;rounded=0;whiteSpace=wrap;html=1;strokeColor=#111111;fillColor=#ffffff;fontFamily=Georgia;fontSize=14;fontColor=$fontColor;align=center;verticalAlign=middle;spacingLeft=0;spacingRight=0;"
}

function Label-Style {
    return 'text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=17;fontStyle=1;fontFamily=Georgia;'
}

function Get-AnchorStyle {
    param(
        [hashtable]$SourceGeometry,
        [hashtable]$TargetGeometry
    )

    $sx = $SourceGeometry.X + ($SourceGeometry.Width / 2.0)
    $sy = $SourceGeometry.Y + ($SourceGeometry.Height / 2.0)
    $tx = $TargetGeometry.X + ($TargetGeometry.Width / 2.0)
    $ty = $TargetGeometry.Y + ($TargetGeometry.Height / 2.0)

    if ([Math]::Abs($sx - $tx) -gt [Math]::Abs($sy - $ty)) {
        if ($sx -lt $tx) {
            return 'exitX=1;exitY=0.5;entryX=0;entryY=0.5;'
        }

        return 'exitX=0;exitY=0.5;entryX=1;entryY=0.5;'
    }

    if ($sy -lt $ty) {
        return 'exitX=0.5;exitY=1;entryX=0.5;entryY=0;'
    }

    return 'exitX=0.5;exitY=0;entryX=0.5;entryY=1;'
}

$pageWidth = 2000
$pageHeight = 1300
$edgeBaseStyle = 'edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=classic;endFill=1;strokeWidth=2;'

$tables = @(
    @{
        Id = 'users'; Title = 'USERS'; X = 110; Y = 180
        Fields = @(
            @{ Name = 'user_id'; Kind = 'pk' },
            @{ Name = 'email'; Kind = 'plain' },
            @{ Name = 'password_hash'; Kind = 'plain' },
            @{ Name = 'first_name'; Kind = 'plain' },
            @{ Name = 'last_name'; Kind = 'plain' },
            @{ Name = 'created_at'; Kind = 'plain' }
        )
    },
    @{
        Id = 'portfolios'; Title = 'PORTFOLIOS'; X = 110; Y = 280
        Fields = @(
            @{ Name = 'portfolio_id'; Kind = 'pk' },
            @{ Name = 'user_id'; Kind = 'fk' },
            @{ Name = 'portfolio_name'; Kind = 'plain' },
            @{ Name = 'description'; Kind = 'plain' },
            @{ Name = 'currency'; Kind = 'plain' },
            @{ Name = 'created_at'; Kind = 'plain' }
        )
    },
    @{
        Id = 'transactions'; Title = 'TRANSACTIONS'; X = 110; Y = 400
        Fields = @(
            @{ Name = 'transaction_id'; Kind = 'pk' },
            @{ Name = 'portfolio_id'; Kind = 'fk' },
            @{ Name = 'instrument_id'; Kind = 'fk' },
            @{ Name = 'transaction_type'; Kind = 'plain' },
            @{ Name = 'quantity'; Kind = 'plain' },
            @{ Name = 'price_per_unit'; Kind = 'plain' },
            @{ Name = 'transaction_date'; Kind = 'plain' },
            @{ Name = 'fees'; Kind = 'plain' },
            @{ Name = 'notes'; Kind = 'plain' }
        )
    },
    @{
        Id = 'holdings'; Title = 'PORTFOLIO_HOLDINGS'; X = 170; Y = 520
        Fields = @(
            @{ Name = 'portfolio_id'; Kind = 'pfk' },
            @{ Name = 'instrument_id'; Kind = 'pfk' },
            @{ Name = 'quantity'; Kind = 'plain' },
            @{ Name = 'average_cost'; Kind = 'plain' },
            @{ Name = 'last_updated'; Kind = 'plain' }
        )
    },
    @{
        Id = 'fi'; Title = 'FINANCIAL_INSTRUMENTS'; X = 820; Y = 180
        Fields = @(
            @{ Name = 'instrument_id'; Kind = 'pk' },
            @{ Name = 'ticker_symbol'; Kind = 'plain' },
            @{ Name = 'instrument_type'; Kind = 'plain' },
            @{ Name = 'name'; Kind = 'plain' },
            @{ Name = 'current_price'; Kind = 'plain' },
            @{ Name = 'last_updated'; Kind = 'plain' }
        )
    },
    @{
        Id = 'historical'; Title = 'HISTORICAL_PRICES'; X = 980; Y = 290
        Fields = @(
            @{ Name = 'price_id'; Kind = 'pk' },
            @{ Name = 'instrument_id'; Kind = 'fk' },
            @{ Name = 'price_date'; Kind = 'plain' },
            @{ Name = 'open_price'; Kind = 'plain' },
            @{ Name = 'high_price'; Kind = 'plain' },
            @{ Name = 'low_price'; Kind = 'plain' },
            @{ Name = 'close_price'; Kind = 'plain' },
            @{ Name = 'adjusted_close'; Kind = 'plain' },
            @{ Name = 'volume'; Kind = 'plain' }
        )
    },
    @{
        Id = 'intraday'; Title = 'INTRADAY_PRICES'; X = 980; Y = 390
        Fields = @(
            @{ Name = 'intraday_price_id'; Kind = 'pk' },
            @{ Name = 'instrument_id'; Kind = 'fk' },
            @{ Name = 'price_time_utc'; Kind = 'plain' },
            @{ Name = 'open_price'; Kind = 'plain' },
            @{ Name = 'high_price'; Kind = 'plain' },
            @{ Name = 'low_price'; Kind = 'plain' },
            @{ Name = 'close_price'; Kind = 'plain' },
            @{ Name = 'volume'; Kind = 'plain' }
        )
    },
    @{
        Id = 'realtime'; Title = 'REALTIME_PRICE_SNAPSHOTS'; X = 980; Y = 490
        Fields = @(
            @{ Name = 'realtime_price_snapshot_id'; Kind = 'pk' },
            @{ Name = 'instrument_id'; Kind = 'fk' },
            @{ Name = 'snapshot_time_utc'; Kind = 'plain' },
            @{ Name = 'source_time_utc'; Kind = 'plain' },
            @{ Name = 'price'; Kind = 'plain' },
            @{ Name = 'volume'; Kind = 'plain' }
        )
    },
    @{
        Id = 'stocks'; Title = 'STOCKS (Subtype)'; X = 780; Y = 680
        Fields = @(
            @{ Name = 'instrument_id'; Kind = 'pfk' },
            @{ Name = 'industry_id'; Kind = 'fk' },
            @{ Name = 'quote_currency'; Kind = 'plain' },
            @{ Name = 'market_cap'; Kind = 'plain' },
            @{ Name = 'pe_ratio'; Kind = 'plain' },
            @{ Name = 'dividend_yield'; Kind = 'plain' },
            @{ Name = 'exchange_id'; Kind = 'fk' }
        )
    },
    @{
        Id = 'etfs'; Title = 'ETFS (Subtype)'; X = 780; Y = 770
        Fields = @(
            @{ Name = 'instrument_id'; Kind = 'pfk' },
            @{ Name = 'asset_class'; Kind = 'plain' },
            @{ Name = 'expense_ratio'; Kind = 'plain' },
            @{ Name = 'issuer'; Kind = 'plain' },
            @{ Name = 'tracking_index'; Kind = 'plain' },
            @{ Name = 'quote_currency'; Kind = 'plain' },
            @{ Name = 'exchange_id'; Kind = 'fk' }
        )
    },
    @{
        Id = 'crypto'; Title = 'CRYPTOCURRENCIES (Subtype)'; X = 780; Y = 860
        Fields = @(
            @{ Name = 'instrument_id'; Kind = 'pfk' },
            @{ Name = 'base_asset_symbol'; Kind = 'plain' },
            @{ Name = 'quote_currency'; Kind = 'plain' },
            @{ Name = 'blockchain'; Kind = 'plain' },
            @{ Name = 'hashing_algorithm'; Kind = 'plain' },
            @{ Name = 'max_supply'; Kind = 'plain' },
            @{ Name = 'circulating_supply'; Kind = 'plain' }
        )
    },
    @{
        Id = 'exchanges'; Title = 'STOCK_EXCHANGES'; X = 1540; Y = 690
        Fields = @(
            @{ Name = 'exchange_id'; Kind = 'pk' },
            @{ Name = 'mic_code'; Kind = 'plain' },
            @{ Name = 'name'; Kind = 'plain' },
            @{ Name = 'country'; Kind = 'plain' },
            @{ Name = 'city'; Kind = 'plain' },
            @{ Name = 'timezone'; Kind = 'plain' }
        )
    },
    @{
        Id = 'sectors'; Title = 'SECTORS'; X = 1540; Y = 830
        Fields = @(
            @{ Name = 'sector_id'; Kind = 'pk' },
            @{ Name = 'sector_name'; Kind = 'plain' }
        )
    },
    @{
        Id = 'industries'; Title = 'INDUSTRIES'; X = 1540; Y = 920
        Fields = @(
            @{ Name = 'industry_id'; Kind = 'pk' },
            @{ Name = 'industry_name'; Kind = 'plain' },
            @{ Name = 'sector_id'; Kind = 'fk' }
        )
    }
)

$relationships = @(
    @{ SourceTable = 'portfolios'; SourceField = 'user_id'; TargetTable = 'users'; TargetField = 'user_id' },
    @{ SourceTable = 'transactions'; SourceField = 'portfolio_id'; TargetTable = 'portfolios'; TargetField = 'portfolio_id' },
    @{ SourceTable = 'transactions'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'holdings'; SourceField = 'portfolio_id'; TargetTable = 'portfolios'; TargetField = 'portfolio_id' },
    @{ SourceTable = 'holdings'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'historical'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'intraday'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'realtime'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'stocks'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'etfs'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'crypto'; SourceField = 'instrument_id'; TargetTable = 'fi'; TargetField = 'instrument_id' },
    @{ SourceTable = 'stocks'; SourceField = 'industry_id'; TargetTable = 'industries'; TargetField = 'industry_id' },
    @{ SourceTable = 'stocks'; SourceField = 'exchange_id'; TargetTable = 'exchanges'; TargetField = 'exchange_id' },
    @{ SourceTable = 'etfs'; SourceField = 'exchange_id'; TargetTable = 'exchanges'; TargetField = 'exchange_id' },
    @{ SourceTable = 'industries'; SourceField = 'sector_id'; TargetTable = 'sectors'; TargetField = 'sector_id' }
)

$allGeometries = @{}
foreach ($table in $tables) {
    $allGeometries[$table.Id] = New-TableGeometry $table
}

function Add-Page {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$DiagramId,
        [string]$PageName,
        [string]$Prefix,
        [string]$Title,
        [string]$BottomNote,
        [bool]$WithEdges
    )

    Add-Line $Builder "  <diagram id=""$DiagramId"" name=""$PageName"">"
    Add-Line $Builder "    <mxGraphModel dx=""1800"" dy=""1200"" grid=""1"" gridSize=""10"" guides=""1"" tooltips=""1"" connect=""1"" arrows=""1"" fold=""1"" page=""1"" pageScale=""1"" pageWidth=""$pageWidth"" pageHeight=""$pageHeight"" math=""0"" shadow=""0"">"
    Add-Line $Builder '      <root>'
    Add-Line $Builder '        <mxCell id="0"/>'
    Add-Line $Builder '        <mxCell id="1" parent="0"/>'

    Add-Line $Builder "        <mxCell id=""${Prefix}_title"" value=""$(Escape-Xml $Title)"" style=""text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=28;fontStyle=1;"" vertex=""1"" parent=""1"">"
    Add-Line $Builder '          <mxGeometry x="50" y="20" width="1300" height="40" as="geometry"/>'
    Add-Line $Builder '        </mxCell>'

    $legendHtml = "<div style='border:2px solid #111;background:#fff;padding:10px 16px;width:250px;font-family:Arial,Helvetica,sans-serif;'><div style='color:#C0392B;font-size:18px;text-align:center;'>Text: Primary Key</div><div style='color:#27AE60;font-size:18px;text-align:center;margin-top:6px;'>Text: Foreign Key</div></div>"
    Add-Line $Builder "        <mxCell id=""${Prefix}_legend"" value=""$(Escape-Xml $legendHtml)"" style=""shape=rect;html=1;strokeColor=none;fillColor=none;whiteSpace=wrap;rounded=0;"" vertex=""1"" parent=""1"">"
    Add-Line $Builder '          <mxGeometry x="120" y="70" width="300" height="90" as="geometry"/>'
    Add-Line $Builder '        </mxCell>'

    Add-Line $Builder "        <mxCell id=""${Prefix}_subtype_box"" value="""" style=""shape=rect;html=1;rounded=0;fillColor=none;strokeColor=#666666;dashed=1;strokeWidth=2;"" vertex=""1"" parent=""1"">"
    Add-Line $Builder '          <mxGeometry x="740" y="640" width="760" height="300" as="geometry"/>'
    Add-Line $Builder '        </mxCell>'

    foreach ($table in $tables) {
        $tableGeometry = $allGeometries[$table.Id]
        $labelId = New-LabelId -Prefix $Prefix -TableId $table.Id
        Add-Line $Builder "        <mxCell id=""$labelId"" value=""$(Escape-Xml $table.Title)"" style=""$(Label-Style)"" vertex=""1"" parent=""1"">"
        Add-Line $Builder "          <mxGeometry x=""$($tableGeometry.Label.X)"" y=""$($tableGeometry.Label.Y)"" width=""$([Math]::Max($tableGeometry.TableWidth, 250))"" height=""$($tableGeometry.Label.Height)"" as=""geometry""/>"
        Add-Line $Builder '        </mxCell>'

        foreach ($field in $table.Fields) {
            $fieldId = New-FieldId -Prefix $Prefix -TableId $table.Id -FieldName $field.Name
            $fieldGeometry = $tableGeometry.Fields[$field.Name]
            Add-Line $Builder "        <mxCell id=""$fieldId"" value=""$(Escape-Xml $field.Name)"" style=""$(Field-Style $field.Kind)"" vertex=""1"" parent=""1"">"
            Add-Line $Builder "          <mxGeometry x=""$($fieldGeometry.X)"" y=""$($fieldGeometry.Y)"" width=""$($fieldGeometry.Width)"" height=""$($fieldGeometry.Height)"" as=""geometry""/>"
            Add-Line $Builder '        </mxCell>'
        }
    }

    Add-Line $Builder "        <mxCell id=""${Prefix}_note"" value=""$(Escape-Xml $BottomNote)"" style=""text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=top;whiteSpace=wrap;fontSize=18;"" vertex=""1"" parent=""1"">"
    Add-Line $Builder '          <mxGeometry x="70" y="1135" width="1100" height="120" as="geometry"/>'
    Add-Line $Builder '        </mxCell>'

    if ($WithEdges) {
        $edgeIndex = 1
        foreach ($relationship in $relationships) {
            $sourceId = New-FieldId -Prefix $Prefix -TableId $relationship.SourceTable -FieldName $relationship.SourceField
            $targetId = New-FieldId -Prefix $Prefix -TableId $relationship.TargetTable -FieldName $relationship.TargetField
            $sourceGeometry = $allGeometries[$relationship.SourceTable].Fields[$relationship.SourceField]
            $targetGeometry = $allGeometries[$relationship.TargetTable].Fields[$relationship.TargetField]
            $anchors = Get-AnchorStyle -SourceGeometry $sourceGeometry -TargetGeometry $targetGeometry
            Add-Line $Builder "        <mxCell id=""${Prefix}_edge_$edgeIndex"" style=""$edgeBaseStyle$anchors"" edge=""1"" parent=""1"" source=""$sourceId"" target=""$targetId"">"
            Add-Line $Builder '          <mxGeometry relative="1" as="geometry"/>'
            Add-Line $Builder '        </mxCell>'
            $edgeIndex++
        }
    }

    Add-Line $Builder '      </root>'
    Add-Line $Builder '    </mxGraphModel>'
    Add-Line $Builder '  </diagram>'
}

$builder = [System.Text.StringBuilder]::new()
Add-Line $builder '<mxfile host="app.diagrams.net" version="24.7.17" type="device">'

Add-Page -Builder $builder -DiagramId 'with-arrows' -PageName 'With Arrows' -Prefix 'wa' `
    -Title '4.3 Normalization to Third Normal Form (3NF) - With Relationship Arrows' `
    -BottomNote 'Arrows = Foreign Key relationships&#xa;Show table dependencies&#xa;Ensure data consistency&#xa;Represent 1:N or M:N relationships&#xa;Help in query joins and database integrity' `
    -WithEdges $true

Add-Page -Builder $builder -DiagramId 'without-arrows' -PageName 'Without Arrows' -Prefix 'wo' `
    -Title '4.3 Normalization to Third Normal Form (3NF) - Structure Only' `
    -BottomNote 'Same structure and colors, but without relationship arrows so you can add your own connectors manually.' `
    -WithEdges $false

Add-Line $builder '</mxfile>'

[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "Wrote $OutputPath"
