# Ensure ReSharper CLI is installed
if (-not (dotnet tool list -g | Select-String -SimpleMatch 'jetbrains.resharper.globaltools')) {
    dotnet tool install -g JetBrains.ReSharper.GlobalTools
}

$path = 'inspect.xml'
if (Test-Path -LiteralPath $path -PathType Leaf) {
    Remove-Item -LiteralPath $path -Force
}

# Run inspection on just the WPF project (skips test/tooling projects in the solution).
# Args are single-quoted so PowerShell's native-command arg pass doesn't mangle the dot in inspect.xml.
jb inspectcode 'src/NetworkTrayAppWPF.csproj' '-f=Xml' "-o=$path" '--severity=SUGGESTION'

# Regroup by category
[xml]$r = Get-Content $path
$types = @{}
foreach ($t in $r.Report.IssueTypes.IssueType) { $types[$t.Id] = $t }

$r.SelectNodes('//Issues/Project/Issue') |
    ForEach-Object {
        [pscustomobject]@{
            Project  = $_.ParentNode.Name
            Category = $types[$_.TypeId].CategoryId
            Severity = $types[$_.TypeId].Severity
            TypeId   = $_.TypeId
            File     = $_.File
            Line     = $_.Line
            Message  = $_.Message
        }
    } |
    Sort-Object Category, Severity, TypeId |
    Group-Object Category |
    ForEach-Object {
        "`n=== $($_.Name) ($($_.Count)) ==="
        $_.Group |
            Format-Table Severity, Project, TypeId, File, Line, Message |
            Out-String -Width 32766
    }

Read-Host "Press Enter to exit"
