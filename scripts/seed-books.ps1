param(
    [string]$Region = $env:AWS_REGION,
    [string]$TableName = $(if ($env:TABLE_NAME) { $env:TABLE_NAME } else { "BookInventory$($env:STACK_POSTFIX)" })
)

$ErrorActionPreference = 'Stop'
if (-not $Region) { throw 'Set AWS_REGION or pass -Region.' }
$month = (Get-Date).ToUniversalTime().ToString('yyyyMM')
$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$books = @(
    @('34b1d665-3ef0-4921-ada0-94954b2e2b2f','The Locked Room Ledger','Mara Ellison','9781736204101','Northbridge Press','Hardcover','Mystery','Like New','7.49','4',"A forensic accountant follows clues hidden in an old bookseller's ledger.",'2022'),
    @('abc30e07-4db3-4efd-bb40-7eaf8fb333c9','Midnight at Ashcroft','Daniel Rook','9781736204102','Hawthorn House','Hardcover','Mystery','Good','9.25','7','A village archivist investigates a disappearance during a winter blackout.','2020'),
    @('681909c1-ab92-442d-9517-b7727bfbd6b2','Orbit of Glass','Priya Sen','9781736204103','Zenith Books','Hardcover','Science Fiction','Very Good','14.99','3','Engineers aboard an orbital habitat race to repair a failing shield.','2023'),
    @('09ef49f5-5d75-46be-8eaf-e80bc175f5e3','The Last Harbor Signal','Evan Cole','9781736204104','Blue Quay Publishing','Paperback','Thriller','Acceptable','6.95','8','A radio operator receives a distress call from an abandoned lighthouse.','2019'),
    @('df8f3244-5850-4fae-ab0b-b643a7f2bff0','Practical Serverless .NET','Nina Kapoor','9781736204105','Builder Press','Hardcover','Technology','Like New','18.50','5','Patterns for building event-driven .NET applications on AWS.','2025')
)

foreach ($book in $books) {
    $item = @{
        BookId=@{S=$book[0]}; Name=@{S=$book[1]}; Author=@{S=$book[2]}; ISBN=@{S=$book[3]}
        Publisher=@{S=$book[4]}; BookType=@{S=$book[5]}; Genre=@{S=$book[6]}; Condition=@{S=$book[7]}
        Price=@{N=$book[8]}; Quantity=@{N=$book[9]}; Summary=@{S=$book[10]}; Year=@{N=$book[11]}
        CreatedBy=@{S='sample-seed'}; CreatedOn=@{S=$now}; LastUpdated=@{S=$now}
        GSI1PK=@{S=$month}; GSI1SK=@{S=$book[0]}
    } | ConvertTo-Json -Compress -Depth 4
    aws dynamodb put-item --region $Region --table-name $TableName --item $item | Out-Null
    Write-Host "Seeded $($book[1])"
}
