#requires -Version 7.3
<#
.SYNOPSIS
Builds Alva in an isolated Compose project and checks its public HTTP API.
.DESCRIPTION
Creates disposable PostgreSQL and Data Protection volumes. The finally block
removes only this run's uniquely named project, volumes and locally built image.
It never connects to an existing Alva database or reads the user's .env file.
#>
[CmdletBinding()]
param([switch] $AuthOnly, [switch] $ActorsOnly, [switch] $DataOnly)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString('N')
$project = "alva-smoke-$runId"
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) $project
$overridePath = Join-Path $temporaryDirectory 'compose.override.yaml'
$emptyEnvironment = Join-Path $temporaryDirectory '.env'
$compose = @('compose', '--project-directory', $repo, '--env-file', $emptyEnvironment,
    '-p', $project, '-f', (Join-Path $repo 'compose.yaml'), '-f', $overridePath)
$environmentOverrides = @{
    POSTGRES_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    POSTGRES_DB = 'alva'; POSTGRES_USER = 'alva'; ALVA_PORT = '0'
    ASPNETCORE_ENVIRONMENT = 'Production'; REVERSE_PROXY_ENABLED = 'false'
    COMPOSE_PROFILES = ''; COMPOSE_ENV_FILES = ''
}
$previousEnvironment = @{}
$clients = [Collections.Generic.List[Net.Http.HttpClient]]::new()
$started = $false

function Invoke-Compose {
    param([string[]] $Arguments)
    & docker @compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $($Arguments[0])" }
}

function New-ApiClient {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.CookieContainer = [Net.CookieContainer]::new()
    $client = [Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [Uri]$script:baseUrl
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    $clients.Add($client)
    return $client
}

function Invoke-Api {
    param(
        [Net.Http.HttpClient] $Client,
        [string] $Method,
        [string] $Path,
        [object] $Body = $null,
        [int[]] $ExpectedStatus = @(200),
        [switch] $WithoutRequestHeader,
        [switch] $Raw,
        [switch] $Bytes,
        [switch] $UnencodedBody
    )
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Path)
    if (-not $WithoutRequestHeader) { [void]$request.Headers.TryAddWithoutValidation('X-Alva-Request', '1') }
    if ($null -ne $Body) {
        if ($Body -is [byte[]]) { $request.Content = [Net.Http.ByteArrayContent]::new($Body) }
        elseif ($UnencodedBody) { $request.Content = [Net.Http.StringContent]::new([string]$Body, [Text.Encoding]::UTF8, 'application/octet-stream') }
        else {
            $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 20 -Compress),
                [Text.Encoding]::UTF8, 'application/json')
        }
    }
    $response = $null
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -notin $ExpectedStatus) {
            throw "$Method $Path returned $([int]$response.StatusCode), expected $($ExpectedStatus -join '/')."
        }
        if ($Bytes) { return ,$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() }
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($Raw) { return $content }
        if ($content -and $response.Content.Headers.ContentType.MediaType -match 'json') {
            return ConvertFrom-Json -InputObject $content -Depth 30
        }
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
    }
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

try {
    [void](New-Item -ItemType Directory -Path $temporaryDirectory)
    [IO.File]::WriteAllText($emptyEnvironment, '')
    # A fixed free port: an ephemeral one is reassigned when the app restarts below.
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $probe.Start()
    $hostPort = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
    $probe.Stop()
    # Override network IPAM to let Docker choose a free subnet; PostgreSQL is private.
    [IO.File]::WriteAllText($overridePath, @"
services:
  db:
    ports: !reset []
  app:
    ports: !override ["127.0.0.1:${hostPort}:8080"]
networks:
  proxy:
    ipam:
      config: !reset []
"@)
    foreach ($name in $environmentOverrides.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $environmentOverrides[$name], 'Process')
    }
    Invoke-Compose -Arguments @('config', '--quiet')
    $started = $true
    Invoke-Compose -Arguments @('up', '--build', '-d', '--wait', '--wait-timeout', '180', 'app')
    $address = (Invoke-Compose -Arguments @('port', 'app', '8080') | Select-Object -Last 1).Trim()
    Assert-True ($address -match '^127\.0\.0\.1:\d+$') 'Compose did not publish a loopback HTTP port.'
    $script:baseUrl = "http://$address/"
    $anonymous = New-ApiClient
    [void](Invoke-Api $anonymous GET 'health/live')
    [void](Invoke-Api $anonymous GET 'health/ready')
    $index = Invoke-Api $anonymous GET '' -Raw
    Assert-True (-not $index.Contains('#[.{fingerprint}]')) 'Published PWA contains unresolved asset placeholders.'
    $bootstrap = [regex]::Match($index, 'src="(?<url>_framework/blazor\.webassembly[^\"]*\.js)"').Groups['url'].Value
    Assert-True ([bool]$bootstrap) 'Published PWA is missing its Blazor bootstrap script.'
    [void](Invoke-Api $anonymous GET $bootstrap -Raw)
    $map = [regex]::Match($index, '<script type="importmap">(?<json>[\s\S]*?)</script>').Groups['json'].Value
    $runtime = '_framework/dotnet.js'
    if ($map.Trim()) {
        $imports = ($map | ConvertFrom-Json).imports
        if ($imports.'./_framework/dotnet.js') { $runtime = $imports.'./_framework/dotnet.js' }
    }
    # A hosted deployment may deliberately use stable filenames instead of an import map.
    [void](Invoke-Api $anonymous GET $runtime -Raw)
    [void](Invoke-Api $anonymous GET '_content/Flare.Components/css/flare-components.css' -Raw)
    [void](Invoke-Api $anonymous GET 'manifest.webmanifest' -Raw)
    [void](Invoke-Api $anonymous GET 'service-worker.js' -Raw)
    Write-Host 'PASS: PostgreSQL readiness and published PWA assets.'

    [void](Invoke-Api $anonymous GET 'api/workspaces' -ExpectedStatus @(401))
    $owner = New-ApiClient
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $registration = @{ email = "owner-$runId@example.invalid"; password = $password;
        displayName = 'Smoke owner'; workspaceName = 'Smoke workspace' }
    [void](Invoke-Api $owner POST 'api/auth/register' $registration -ExpectedStatus @(200, 201))
    $workspaces = Invoke-Api $owner GET 'api/workspaces'
    Assert-True ($workspaces.Count -eq 1) 'Registration should create exactly one workspace.'
    $workspaceId = $workspaces[0].id
    $teams = Invoke-Api $owner GET "api/workspaces/$workspaceId/teams"
    Assert-True ($teams.Count -ge 1) 'Registration did not create a team.'
    $teamId = $teams[0].id
    [void](Invoke-Api $owner POST "api/workspaces/$workspaceId/teams" @{ name = 'No CSRF header' } `
        -WithoutRequestHeader -ExpectedStatus @(400, 403))

    $stranger = New-ApiClient
    [void](Invoke-Api $stranger POST 'api/auth/register' @{
        email = "stranger-$runId@example.invalid"; password = $password;
        displayName = 'Other tenant'; workspaceName = 'Other workspace'
    } -ExpectedStatus @(200, 201))
    [void](Invoke-Api $stranger GET "api/workspaces/$workspaceId/teams" -ExpectedStatus @(403, 404))
    Write-Host 'PASS: registration, cookie session, CSRF header and tenant isolation.'
    $manager = New-ApiClient
    $managerEmail = "manager-$runId@example.invalid"
    [void](Invoke-Api $manager POST 'api/auth/register' @{
        email = $managerEmail; password = $password; displayName = 'Second owner'; workspaceName = 'Manager workspace'
    } -ExpectedStatus @(200, 201))
    [void](Invoke-Api $owner POST "api/workspaces/$workspaceId/members" @{
        email = $managerEmail; teamIds = @($teamId); isOwner = $true
    } -ExpectedStatus @(204))
    $demotions = @()
    $responses = @()
    try {
        foreach ($item in @(@{ client = $owner; email = $registration.email }, @{ client = $manager; email = $managerEmail })) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "api/workspaces/$workspaceId/members")
            [void]$request.Headers.TryAddWithoutValidation('X-Alva-Request', '1')
            $request.Content = [Net.Http.StringContent]::new((@{
                email = $item.email; teamIds = @($teamId); isOwner = $false
            } | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
            $demotions += @{ request = $request; pending = $item.client.SendAsync($request) }
        }
        foreach ($item in $demotions) { $responses += $item.pending.GetAwaiter().GetResult() }
        $statuses = @($responses | ForEach-Object { [int]$_.StatusCode } | Sort-Object)
        Assert-True (($statuses -join ',') -eq '204,400') 'Concurrent owner demotion must preserve exactly one owner.'
        $remainingOwner = if ([int]$responses[0].StatusCode -eq 204) { $manager } else { $owner }
        $members = Invoke-Api $remainingOwner GET "api/workspaces/$workspaceId/members"
        Assert-True (@($members | Where-Object isOwner).Count -eq 1) 'Workspace lost its final owner.'
    }
    finally {
        foreach ($response in $responses) { $response.Dispose() }
        foreach ($item in $demotions) { $item.request.Dispose() }
    }
    Write-Host 'PASS: concurrent owner changes preserve a workspace owner.'
    if ($AuthOnly) {
        [void](Invoke-Api $owner POST 'api/auth/logout' -ExpectedStatus @(200, 204))
        [void](Invoke-Api $owner GET 'api/workspaces' -ExpectedStatus @(401))
        [void](Invoke-Api $owner POST 'api/auth/login' @{ email = $registration.email; password = $password } `
            -ExpectedStatus @(200, 204))
        Write-Host 'Alva authentication smoke passed (business checks not requested).'
        return
    }
    [void](Invoke-Api $anonymous GET 'api/does-not-exist' -ExpectedStatus @(404))

    $department = Invoke-Api $owner POST "api/teams/$teamId/actors" @{ name = 'Engineering'; kind = 'Department' } -ExpectedStatus @(200, 201)
    $role = Invoke-Api $owner POST "api/teams/$teamId/actors" @{ name = 'Developer'; kind = 'Role' } -ExpectedStatus @(200, 201)
    $person = Invoke-Api $owner POST "api/teams/$teamId/actors" @{
        name = 'Smoke developer'; kind = 'Person'; parentId = $department.id;
        departmentIds = @($department.id); roleIds = @($role.id)
    } -ExpectedStatus @(200, 201)
    $subDepartment = Invoke-Api $owner POST "api/teams/$teamId/actors" @{
        name = 'Subdepartment'; kind = 'Department'; parentId = $department.id
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner PUT "api/teams/$teamId/actors/$($department.id)" @{
        name = 'Cyclic department'; kind = 'Department'; parentId = $subDepartment.id
    } -ExpectedStatus @(400))
    $actors = Invoke-Api $owner GET "api/teams/$teamId/actors"
    $savedPerson = @($actors | Where-Object id -eq $person.id)[0]
    Assert-True ($savedPerson.roleIds -contains $role.id) 'Person role link was not saved.'
    Assert-True ($savedPerson.departmentIds -contains $department.id) 'Person department link was not saved.'
    $otherWorkspaces = Invoke-Api $stranger GET 'api/workspaces'
    $otherTeams = Invoke-Api $stranger GET "api/workspaces/$($otherWorkspaces[0].id)/teams"
    $foreignRole = Invoke-Api $stranger POST "api/teams/$($otherTeams[0].id)/actors" @{
        name = 'Foreign role'; kind = 'Role'
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner POST "api/teams/$teamId/actors" @{
        name = 'Invalid role link'; kind = 'Person'; roleIds = @($foreignRole.id)
    } -ExpectedStatus @(400))
    [void](Invoke-Api $stranger GET "api/teams/$teamId/actors" -ExpectedStatus @(403, 404))
    Write-Host 'PASS: actor roles, department membership and hierarchy cycle validation.'

    $monday = [DateTime]::Today.AddDays(7 - ([int][DateTime]::Today.DayOfWeek + 6) % 7).ToString('yyyy-MM-dd')
    [void](Invoke-Api $owner POST "api/teams/$teamId/actors/$($department.id)/calendar/exceptions" @{
        from = $monday; to = $monday; availableHours = 0; reason = 'Department holiday'
    } -ExpectedStatus @(200, 201, 204))
    [void](Invoke-Api $owner GET "api/teams/$teamId/actors/$($role.id)/calendar" -ExpectedStatus @(404))
    $capacity = Invoke-Api $owner GET "api/teams/$teamId/capacity?from=$monday&to=$monday"
    $personDay = @($capacity | Where-Object actorId -eq $person.id)[0]
    Assert-True ($null -ne $personDay -and [decimal]$personDay.availableHours -eq 0) 'A department holiday did not reach its member capacity.'
    Write-Host 'PASS: department calendar applies to its members.'
    if ($ActorsOnly) {
        Write-Host 'Alva actor smoke passed (task checks not requested).'
        return
    }
    $portable = Invoke-Api $owner GET "api/teams/$teamId/data/export?format=json"
    Assert-True ($portable.version -eq 1 -and $portable.rows.Count -gt 0) 'Export package is empty or unversioned.'
    [void](Invoke-Api $stranger GET "api/teams/$teamId/data/export?format=json" -ExpectedStatus @(403, 404))
    foreach ($format in @('json', 'csv', 'xlsx')) {
        $payload = Invoke-Api $owner GET "api/teams/$teamId/data/export?format=$format" -Bytes
        $imported = Invoke-Api $owner POST "api/teams/$teamId/data/import?format=$format" $payload
        Assert-True ($imported.imported -gt 0) "$format import did not create records."
        foreach ($entry in $imported.idMap.PSObject.Properties) {
            Assert-True ($entry.Name -ne $entry.Value) "$format import reused a source ID."
        }
    }
    [void](Invoke-Api $stranger POST "api/teams/$teamId/data/import?format=json" $portable -ExpectedStatus @(403, 404))
    $beforeInvalidImport = @(Invoke-Api $owner GET "api/teams/$teamId/actors").Count
    [void](Invoke-Api $owner POST "api/teams/$teamId/data/import?format=json" @{
        version = 1; sourceTeamId = $teamId; rows = @(@{ kind = 'actor'; id = [Guid]::NewGuid(); payload = @{} })
    } -ExpectedStatus @(400))
    Assert-True (@(Invoke-Api $owner GET "api/teams/$teamId/actors").Count -eq $beforeInvalidImport) `
        'Rejected import changed the database.'
    Write-Host 'PASS: JSON/CSV/XLSX graph import, new IDs, tenant guards and rejected import rollback.'
    if ($DataOnly) {
        Write-Host 'Alva data transfer smoke passed (task checks not requested).'
        return
    }
    [void](Invoke-Api $stranger GET "api/teams/$teamId/tasks" -ExpectedStatus @(403, 404))

    $projectDto = Invoke-Api $owner POST "api/teams/$teamId/projects" @{
        name = 'Smoke project'; description = 'Disposable integration data'; teamIds = @($teamId)
    } -ExpectedStatus @(200, 201)
    # A project saved without the team that saves it would vanish from that team's list.
    $sibling = Invoke-Api $remainingOwner POST "api/workspaces/$workspaceId/teams" @{ name = 'Sibling team' } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner POST "api/teams/$teamId/projects" @{
        name = 'Detached project'; description = ''; teamIds = @($sibling.id)
    } -ExpectedStatus @(400))
    $parent = Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Parent'; projectId = $projectDto.id; estimateHours = 8; deadline = '2030-01-31'
    } -ExpectedStatus @(200, 201)
    $child = Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Child'; parentId = $parent.id; projectId = $projectDto.id; estimateHours = 3
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($child.id)/assignment" @{
        targetId = $role.id; resolvedPersonId = $person.id
    } -ExpectedStatus @(200, 204))
    # Assignment is a task edit too; obtain the current version before testing updates.
    $child = @(Invoke-Api $owner GET "api/teams/$teamId/tasks" | Where-Object id -eq $child.id)[0]
    [void](Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($parent.id)/move" @{
        parentId = $child.id; order = 0; version = $parent.version
    } -ExpectedStatus @(400))
    [void](Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Invalid estimate'; estimateHours = -1
    } -ExpectedStatus @(400))
    $updated = Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($child.id)" @{
        title = 'Updated child'; parentId = $parent.id; projectId = $projectDto.id;
        estimateHours = 4; version = $child.version
    } -ExpectedStatus @(200)
    [void](Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($child.id)" @{
        title = 'Stale edit'; version = $child.version
    } -ExpectedStatus @(409))
    Assert-True ($updated.version -gt $child.version) 'A task edit did not advance its version.'

    $otherWorkspaces = Invoke-Api $stranger GET 'api/workspaces'
    $otherTeams = Invoke-Api $stranger GET "api/workspaces/$($otherWorkspaces[0].id)/teams"
    $otherProject = Invoke-Api $stranger POST "api/teams/$($otherTeams[0].id)/projects" @{
        name = 'Foreign project'; description = ''; teamIds = @($otherTeams[0].id)
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Foreign project link'; projectId = $otherProject.id
    } -ExpectedStatus @(400, 403, 404))
    Write-Host 'PASS: project/task writes, cycle rejection, optimistic concurrency and cross-team links.'

    # The Gantt chart reads every link of the team at once.
    $link = Invoke-Api $owner POST "api/teams/$teamId/tasks/$($child.id)/dependencies" @{
        predecessorId = $parent.id; successorId = $child.id; kind = 'SS'; lagDays = 1
    } -ExpectedStatus @(200, 201)
    $teamLinks = @(Invoke-Api $owner GET "api/teams/$teamId/tasks/dependencies")
    Assert-True (@($teamLinks | Where-Object { $_.id -eq $link.id -and $_.kind -eq 'SS' -and $_.lagDays -eq 1 }).Count -eq 1) 'The team dependency list misses a new link.'
    [void](Invoke-Api $stranger GET "api/teams/$teamId/tasks/dependencies" -ExpectedStatus @(403, 404))
    Write-Host 'PASS: team dependency list for the Gantt chart.'

    $sprintStart = [DateTime]::Today.AddDays(7 - ([int][DateTime]::Today.DayOfWeek + 6) % 7)
    $sprint = Invoke-Api $owner POST "api/teams/$teamId/sprints" @{
        name = 'Demand sprint'; goal = ''; startDate = $sprintStart.ToString('yyyy-MM-dd'); endDate = $sprintStart.AddDays(4).ToString('yyyy-MM-dd')
    } -ExpectedStatus @(200, 201)
    $roleTask = Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Role demand'; projectId = $projectDto.id; sprintId = $sprint.id; estimateHours = 10
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($roleTask.id)/assignment" @{ targetId = $role.id } -ExpectedStatus @(200, 204))
    $range = "from=$($sprintStart.ToString('yyyy-MM-dd'))&to=$($sprintStart.AddDays(6).ToString('yyyy-MM-dd'))"
    $demand = @(Invoke-Api $owner GET "api/teams/$teamId/capacity/demand?$range" | Where-Object targetId -eq $role.id)
    Assert-True ($demand.Count -eq 5 -and ($demand | Measure-Object hours -Sum).Sum -eq 10) 'Role work in a sprint was not spread over the sprint working days.'
    Write-Host 'PASS: unassigned role demand follows the sprint window.'

    # The department holiday set above falls into this task's window and must surface as a risk cause.
    $holidayTask = Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Holiday risk'; projectId = $projectDto.id; estimateHours = 4
        startDate = $sprintStart.ToString('yyyy-MM-dd'); endDate = $sprintStart.AddDays(2).ToString('yyyy-MM-dd')
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner PUT "api/teams/$teamId/tasks/$($holidayTask.id)/assignment" @{ targetId = $person.id } -ExpectedStatus @(200, 204))
    $holidayRisk = @(Invoke-Api $owner GET "api/teams/$teamId/risks" | Where-Object taskId -eq $holidayTask.id)[0]
    Assert-True ($null -ne $holidayRisk -and @($holidayRisk.reasons | Where-Object { $_ -like 'Исполнитель недоступен:*' }).Count -eq 1) 'A holiday in the task window was not reported as a risk.'
    Write-Host 'PASS: risks report an unavailable assignee.'

    # A parent always covers its children: a child dated past its parent widens the parent.
    $coverParent = Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Cover parent'; projectId = $projectDto.id
        startDate = $sprintStart.ToString('yyyy-MM-dd'); endDate = $sprintStart.AddDays(2).ToString('yyyy-MM-dd')
    } -ExpectedStatus @(200, 201)
    [void](Invoke-Api $owner POST "api/teams/$teamId/tasks" @{
        title = 'Cover child'; projectId = $projectDto.id; parentId = $coverParent.id
        startDate = $sprintStart.AddDays(-1).ToString('yyyy-MM-dd'); endDate = $sprintStart.AddDays(6).ToString('yyyy-MM-dd')
    } -ExpectedStatus @(200, 201))
    $covered = @(Invoke-Api $owner GET "api/teams/$teamId/tasks" | Where-Object id -eq $coverParent.id)[0]
    Assert-True ($covered.startDate -eq $sprintStart.AddDays(-1).ToString('yyyy-MM-dd') -and $covered.endDate -eq $sprintStart.AddDays(6).ToString('yyyy-MM-dd')) `
        'A parent task did not widen to cover its child.'
    Write-Host 'PASS: a parent task covers its children.'

    # Autobalance runs the OR-Tools native solver inside the Linux container and only proposes changes.
    foreach ($mode in @('Gentle', 'Deadlines', 'Maximum')) {
        $proposal = Invoke-Api $owner POST "api/teams/$teamId/sprints/$($sprint.id)/balance" @{ mode = $mode }
        Assert-True ($proposal.mode -eq $mode -and $proposal.status -in @('Optimal', 'Feasible', 'Heuristic') -and $null -ne $proposal.after) `
            "Autobalance did not return a proposal in $mode mode."
    }
    [void](Invoke-Api $stranger POST "api/teams/$teamId/sprints/$($sprint.id)/balance" @{ mode = 'Gentle' } -ExpectedStatus @(403, 404))
    [void](Invoke-Api $owner POST "api/teams/$teamId/sprints/$([guid]::NewGuid())/balance" @{ mode = 'Gentle' } -ExpectedStatus @(404))
    Write-Host 'PASS: sprint autobalance proposes a plan with the native solver.'

    # Restart the real published API. Cookie keys and business records must persist.
    Invoke-Compose -Arguments @('restart', 'app')
    Invoke-Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '90', 'app')
    $tasks = Invoke-Api $owner GET "api/teams/$teamId/tasks"
    Assert-True (@($tasks | Where-Object id -eq $updated.id).Count -eq 1) 'Task disappeared after API restart.'
    Assert-True (@($tasks | Where-Object { $_.id -eq $updated.id -and $_.title -eq 'Updated child' }).Count -eq 1) `
        'Updated task state did not persist.'
    [void](Invoke-Api $owner POST 'api/auth/logout' -ExpectedStatus @(200, 204))
    [void](Invoke-Api $owner GET 'api/workspaces' -ExpectedStatus @(401))
    [void](Invoke-Api $owner POST 'api/auth/login' @{ email = $registration.email; password = $password } `
        -ExpectedStatus @(200, 204))
    [void](Invoke-Api $owner GET 'api/workspaces')
    Write-Host 'PASS: PostgreSQL persistence, cookie key persistence, logout and login.'
    Write-Host 'Alva integration smoke passed.'
}
finally {
    foreach ($client in $clients) { $client.Dispose() }
    try {
        if ($started) {
            # Exact UUID project; no --remove-orphans, global prune or foreign volumes.
            Invoke-Compose -Arguments @('down', '--volumes', '--rmi', 'local', '--timeout', '10')
        }
    }
    finally {
        foreach ($name in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
        if (Test-Path -LiteralPath $temporaryDirectory) {
            $resolvedTemporary = [IO.Path]::GetFullPath($temporaryDirectory)
            $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
            if ((Split-Path $resolvedTemporary -Parent).TrimEnd('\', '/') -ne $expectedParent -or
                (Split-Path $resolvedTemporary -Leaf) -ne $project) {
                throw 'Refusing cleanup outside the owned temporary directory.'
            }
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}
