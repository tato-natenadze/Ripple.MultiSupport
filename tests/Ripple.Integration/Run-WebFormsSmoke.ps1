param(
    [Parameter(Mandatory = $true)][string]$TestOutput,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$iis = Join-Path $env:ProgramFiles 'IIS Express\iisexpress.exe'
$template = Join-Path $env:ProgramFiles 'IIS Express\AppServer\applicationhost.config'
if (!(Test-Path -LiteralPath $iis)) { throw 'IIS Express is required for this explicit smoke test.' }
$testDll = Join-Path $TestOutput 'Ripple.Tests.dll'
if (!(Test-Path -LiteralPath $testDll)) { throw 'Pass the directory containing the built net472 test assembly.' }
$taskOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$sitePath = Join-Path $taskOutput 'site'
$binPath = Join-Path $sitePath 'bin'
New-Item -ItemType Directory -Path $binPath -Force | Out-Null
Get-ChildItem -LiteralPath $TestOutput -Filter '*.dll' | Copy-Item -Destination $binPath -Force
Copy-Item -LiteralPath (Join-Path $TestOutput 'Fixtures') -Destination $sitePath -Recurse -Force

$webConfig = @'
<?xml version="1.0"?>
<configuration>
  <system.web>
    <compilation targetFramework="4.7.2">
      <assemblies><add assembly="netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51" /></assemblies>
    </compilation>
    <httpRuntime targetFramework="4.7.2" executionTimeout="90" />
    <customErrors mode="Off" />
  </system.web>
</configuration>
'@
[xml]$webXml = $webConfig
$testConfig = Join-Path $TestOutput 'Ripple.Tests.dll.config'
if (Test-Path -LiteralPath $testConfig) {
    [xml]$runtimeConfig = Get-Content -Raw -LiteralPath $testConfig
    if ($runtimeConfig.configuration.runtime) {
        $null = $webXml.configuration.AppendChild($webXml.ImportNode($runtimeConfig.configuration.runtime, $true))
    }
}
$webXml.Save((Join-Path $sitePath 'web.config'))
$page = @'
<%@ Page Language="C#" Async="true" %>
<%@ Assembly Name="Ripple.Tests" %>
<%@ Import Namespace="System.Threading.Tasks" %>
<script runat="server">
protected void Page_Load(object sender, EventArgs e)
{
    RegisterAsyncTask(new PageAsyncTask(async () => {
        var compatibility = new Ripple.Tests.CompatibilityTests();
        compatibility.ExistingPublicTypesAndMembersRemainAvailable();
        compatibility.ExistingTransactionsKeepTheirExactSignedBytes();
        compatibility.GeneratedAddressesRemainValidAndMatchTheirSeeds();
        await new Ripple.Tests.AccountAndHistoryTests().AccountUsesPinnedValidatedLedgerAndDynamicReserve();
        await new Ripple.Tests.AccountAndHistoryTests().HistoryContinuationPinsRangeAndIsBoundToAccount();
        await new Ripple.Tests.PaymentTests().SendSignsLocallyAndWaitsForValidatedResult(
            "sp5fghtJtpUorTwvof1NpDXAzNwf5", "rLUEXYuLiQptky37CqLcm9USQpPiz5rkpD");
        await new Ripple.Tests.PaymentWatchTests().WatchReplaysConfirmedIncomingOnlyAndCheckpointsAfterAcknowledgement();
        Response.ContentType = "application/json";
        Response.Write("{\"webforms\":\"passed\",\"runtime\":\"" + Environment.Version + "\",\"apis\":\"legacy and all five new APIs\"}");
    }));
}
</script>
'@
[System.IO.File]::WriteAllText((Join-Path $sitePath 'Default.aspx'), $page)

# Isolate the entire IIS Express configuration and logs; do not edit the user's IIS setup.
$configText = [System.IO.File]::ReadAllText($template).Replace('%IIS_USER_HOME%', [System.Security.SecurityElement]::Escape($taskOutput))
[xml]$hostXml = $configText
$sites = $hostXml.configuration.'system.applicationHost'.sites
foreach ($oldSite in @($sites.SelectNodes('site'))) { $null = $sites.RemoveChild($oldSite) }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$site = $hostXml.CreateElement('site')
$site.SetAttribute('name', 'RippleSmoke'); $site.SetAttribute('id', '1'); $site.SetAttribute('serverAutoStart', 'true')
$app = $hostXml.CreateElement('application'); $app.SetAttribute('path', '/'); $app.SetAttribute('applicationPool', 'IISExpressAppPool')
$virtual = $hostXml.CreateElement('virtualDirectory'); $virtual.SetAttribute('path', '/'); $virtual.SetAttribute('physicalPath', $sitePath)
$null = $app.AppendChild($virtual); $null = $site.AppendChild($app)
$bindings = $hostXml.CreateElement('bindings'); $binding = $hostXml.CreateElement('binding')
$binding.SetAttribute('protocol', 'http'); $binding.SetAttribute('bindingInformation', (':' + $port + ':localhost'))
$null = $bindings.AppendChild($binding); $null = $site.AppendChild($bindings); $null = $sites.AppendChild($site)
$configPath = Join-Path $taskOutput 'applicationhost.config'
$hostXml.Save($configPath)
$process = $null
try {
    $process = Start-Process -FilePath $iis -ArgumentList ('/config:"' + $configPath + '"'), '/site:RippleSmoke', '/systray:false' `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'iis.stdout.log') -RedirectStandardError (Join-Path $taskOutput 'iis.stderr.log')
    $response = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($process.HasExited) { throw ('IIS Express exited. See logs in ' + $taskOutput) }
        try { $response = Invoke-WebRequest -Uri ('http://localhost:' + $port + '/Default.aspx') -UseBasicParsing -TimeoutSec 60; break }
        catch {
            if ($_.Exception.Response) {
                [System.IO.File]::WriteAllText((Join-Path $taskOutput 'error.html'), [string]$_.ErrorDetails.Message)
                throw ('Web Forms returned an HTTP error; details: ' + (Join-Path $taskOutput 'error.html'))
            }
            Start-Sleep -Milliseconds 250
        }
    }
    if (!$response -or $response.Content -notmatch '"webforms":"passed"') { throw 'Web Forms smoke test did not pass.' }
    [System.IO.File]::WriteAllText((Join-Path $taskOutput 'result.json'), $response.Content)
    $response.Content
}
finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
}
