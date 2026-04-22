using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Localization;

namespace ShadowLink.Services;

public sealed class PlatformFirewallConfigurationService : IFirewallConfigurationService
{
    public Task<FirewallConfigurationStatus> EvaluateAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return EvaluateWindowsAsync(settings, cancellationToken);
        }

        if (OperatingSystem.IsMacOS())
        {
            return EvaluateMacAsync(settings, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return EvaluateLinuxAsync(settings, cancellationToken);
        }

        return Task.FromResult(FirewallConfigurationStatus.Hidden);
    }

    public Task<FirewallConfigurationStatus> EnsureOpenAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return EnsureWindowsOpenAsync(settings, cancellationToken);
        }

        if (OperatingSystem.IsMacOS())
        {
            return EnsureMacOpenAsync(settings, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return EnsureLinuxOpenAsync(settings, cancellationToken);
        }

        return Task.FromResult(FirewallConfigurationStatus.Hidden);
    }

    private static async Task<FirewallConfigurationStatus> EvaluateWindowsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = false,
                IsReady = false,
                Title = ShadowLinkText.Translate("firewall.review_needed"),
                Detail = ShadowLinkText.Translate("firewall.windows.path_missing")
            };
        }

        Boolean hasApplicationRule = await HasWindowsAppRuleAsync(processPath, cancellationToken).ConfigureAwait(false);
        if (hasApplicationRule)
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = true,
                Title = ShadowLinkText.Translate("firewall.open"),
                Detail = ShadowLinkText.Translate("firewall.windows.ready")
            };
        }

        return new FirewallConfigurationStatus
        {
            IsVisible = true,
            IsSupported = true,
            IsReady = false,
            Title = ShadowLinkText.Translate("firewall.allow_incoming"),
            Detail = ShadowLinkText.Translate("firewall.windows.allow_detail"),
            ActionLabel = ShadowLinkText.Translate("firewall.allow_access")
        };
    }

    private static async Task<FirewallConfigurationStatus> EnsureWindowsOpenAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return await EvaluateWindowsAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        String applicationRuleName = BuildWindowsApplicationRuleName();
        String escapedProcessPath = EscapePowerShellSingleQuotedString(processPath);
        String escapedRuleName = EscapePowerShellSingleQuotedString(applicationRuleName);
        String arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetFirewallRule -DisplayName '" + escapedRuleName + "' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue; New-NetFirewallRule -DisplayName '" + escapedRuleName + "' -Direction Inbound -Action Allow -Program '" + escapedProcessPath + "' -Profile Any -ErrorAction SilentlyContinue | Out-Null\"";
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = false,
                Title = ShadowLinkText.Translate("firewall.not_changed"),
                Detail = ShadowLinkText.Translate("firewall.windows.not_changed"),
                ActionLabel = ShadowLinkText.Translate("firewall.allow_access")
            };
        }

        return await EvaluateWindowsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FirewallConfigurationStatus> EvaluateMacAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = false,
                IsReady = false,
                Title = ShadowLinkText.Translate("firewall.review_needed"),
                Detail = ShadowLinkText.Translate("firewall.macos.path_missing")
            };
        }

        (Int32 globalStateExitCode, String globalStateOutput) = await RunCommandAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--getglobalstate", cancellationToken).ConfigureAwait(false);
        if (globalStateExitCode == 0 && ContainsAny(globalStateOutput, "(state = 0)", "disabled", "off"))
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = true,
                Title = ShadowLinkText.Translate("firewall.open"),
                Detail = ShadowLinkText.Translate("firewall.macos.disabled")
            };
        }

        (Int32 listExitCode, String listOutput) = await RunCommandAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--listapps", cancellationToken).ConfigureAwait(false);
        (Int32 blockedExitCode, String blockedOutput) = await RunCommandAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--getappblocked " + QuoteArgument(processPath), cancellationToken).ConfigureAwait(false);
        Boolean isListed = listExitCode == 0 && listOutput.Contains(processPath, StringComparison.OrdinalIgnoreCase);
        Boolean isBlocked = blockedExitCode == 0 && ContainsAny(blockedOutput, "block", "yes", "on");
        if (isListed && !isBlocked)
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = true,
                Title = ShadowLinkText.Translate("firewall.open"),
                Detail = ShadowLinkText.Translate("firewall.macos.ready")
            };
        }

        return new FirewallConfigurationStatus
        {
            IsVisible = true,
            IsSupported = true,
            IsReady = false,
            Title = ShadowLinkText.Translate("firewall.macos.allow_title"),
            Detail = ShadowLinkText.TranslateFormat("firewall.macos.allow_detail", settings.ControlPort),
            ActionLabel = ShadowLinkText.Translate("firewall.allow_access")
        };
    }

    private static async Task<FirewallConfigurationStatus> EnsureMacOpenAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return await EvaluateMacAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        await RunCommandAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--add " + QuoteArgument(processPath), cancellationToken).ConfigureAwait(false);
        await RunCommandAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--unblockapp " + QuoteArgument(processPath), cancellationToken).ConfigureAwait(false);
        return await EvaluateMacAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FirewallConfigurationStatus> EvaluateLinuxAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        (Int32 ufwExitCode, String ufwOutput) = await RunCommandAsync("ufw", "status", cancellationToken).ConfigureAwait(false);
        if (ufwExitCode == 0)
        {
            if (ufwOutput.Contains("inactive", StringComparison.OrdinalIgnoreCase))
            {
                return new FirewallConfigurationStatus
                {
                    IsVisible = true,
                    IsSupported = true,
                    IsReady = true,
                    Title = ShadowLinkText.Translate("firewall.open"),
                    Detail = ShadowLinkText.Translate("firewall.linux.ufw_inactive")
                };
            }

            Boolean hasDiscoveryRule = ufwOutput.Contains(settings.DiscoveryPort + "/udp", StringComparison.OrdinalIgnoreCase);
            Boolean hasControlRule = ufwOutput.Contains(settings.ControlPort + "/tcp", StringComparison.OrdinalIgnoreCase);
            if (hasDiscoveryRule && hasControlRule)
            {
                return new FirewallConfigurationStatus
                {
                    IsVisible = true,
                    IsSupported = true,
                    IsReady = true,
                    Title = ShadowLinkText.Translate("firewall.open"),
                    Detail = ShadowLinkText.TranslateFormat("firewall.linux.ufw_ready", settings.DiscoveryPort, settings.ControlPort)
                };
            }

            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = false,
                Title = ShadowLinkText.Translate("firewall.allow_incoming"),
                Detail = ShadowLinkText.TranslateFormat("firewall.linux.ufw_allow", settings.DiscoveryPort, settings.ControlPort),
                ActionLabel = ShadowLinkText.Translate("firewall.allow_access")
            };
        }

        (Int32 discoveryExitCode, _) = await RunCommandAsync("firewall-cmd", "--query-port=" + settings.DiscoveryPort + "/udp", cancellationToken).ConfigureAwait(false);
        (Int32 controlExitCode, _) = await RunCommandAsync("firewall-cmd", "--query-port=" + settings.ControlPort + "/tcp", cancellationToken).ConfigureAwait(false);
        if (discoveryExitCode == 0 && controlExitCode == 0)
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = true,
                Title = ShadowLinkText.Translate("firewall.open"),
                Detail = ShadowLinkText.TranslateFormat("firewall.linux.firewalld_ready", settings.DiscoveryPort, settings.ControlPort)
            };
        }

        (Int32 firewalldStateExitCode, String firewalldStateOutput) = await RunCommandAsync("firewall-cmd", "--state", cancellationToken).ConfigureAwait(false);
        if (firewalldStateExitCode == 0 && firewalldStateOutput.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
            return new FirewallConfigurationStatus
            {
                IsVisible = true,
                IsSupported = true,
                IsReady = false,
                Title = ShadowLinkText.Translate("firewall.allow_incoming"),
                Detail = ShadowLinkText.TranslateFormat("firewall.linux.firewalld_allow", settings.DiscoveryPort, settings.ControlPort),
                ActionLabel = ShadowLinkText.Translate("firewall.allow_access")
            };
        }

        return new FirewallConfigurationStatus
        {
            IsVisible = true,
            IsSupported = false,
            IsReady = false,
            Title = ShadowLinkText.Translate("firewall.review_needed"),
            Detail = ShadowLinkText.TranslateFormat("firewall.linux.manual", settings.DiscoveryPort, settings.ControlPort)
        };
    }

    private static async Task<FirewallConfigurationStatus> EnsureLinuxOpenAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        (Int32 ufwExitCode, _) = await RunCommandAsync("ufw", "allow " + settings.DiscoveryPort + "/udp", cancellationToken).ConfigureAwait(false);
        if (ufwExitCode == 0)
        {
            await RunCommandAsync("ufw", "allow " + settings.ControlPort + "/tcp", cancellationToken).ConfigureAwait(false);
            return await EvaluateLinuxAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        (Int32 firewalldExitCode, _) = await RunCommandAsync("firewall-cmd", "--add-port=" + settings.DiscoveryPort + "/udp", cancellationToken).ConfigureAwait(false);
        if (firewalldExitCode == 0)
        {
            await RunCommandAsync("firewall-cmd", "--add-port=" + settings.ControlPort + "/tcp", cancellationToken).ConfigureAwait(false);
            await RunCommandAsync("firewall-cmd", "--permanent --add-port=" + settings.DiscoveryPort + "/udp", cancellationToken).ConfigureAwait(false);
            await RunCommandAsync("firewall-cmd", "--permanent --add-port=" + settings.ControlPort + "/tcp", cancellationToken).ConfigureAwait(false);
            return await EvaluateLinuxAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        return await EvaluateLinuxAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Boolean> HasWindowsAppRuleAsync(String processPath, CancellationToken cancellationToken)
    {
        String command = "-NoProfile -ExecutionPolicy Bypass -Command \"$rule = Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object { $_.Program -eq '" + EscapePowerShellSingleQuotedString(processPath) + "' }; if ($null -eq $rule) { Write-Output 'False' } else { Write-Output 'True' }\"";
        (Int32 exitCode, String output) = await RunCommandAsync("powershell.exe", command, cancellationToken).ConfigureAwait(false);
        return exitCode == 0 && output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static String BuildWindowsApplicationRuleName()
    {
        return "ShadowLink Application";
    }

    private static String EscapePowerShellSingleQuotedString(String value)
    {
        return value.Replace("'", "''");
    }

    private static String QuoteArgument(String value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static Boolean ContainsAny(String value, params String[] patterns)
    {
        foreach (String pattern in patterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(Int32 ExitCode, String Output)> RunCommandAsync(String fileName, String arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using Process process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();
            Task<String> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<String> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            String[] outputs = await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            StringBuilder outputBuilder = new StringBuilder(outputs[0].Length + outputs[1].Length);
            outputBuilder.Append(outputs[0]);
            outputBuilder.Append(outputs[1]);
            return (process.ExitCode, outputBuilder.ToString());
        }
        catch (Win32Exception)
        {
            return (-1, String.Empty);
        }
    }
}
