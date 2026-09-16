namespace Flint.Cli;

/// <summary>The shell completion script `flint completion powershell` prints.</summary>
/// <remarks>
/// Printed rather than installed. Writing to somebody's PowerShell profile without being asked is
/// not something a diagnostics tool should do, and the usual idiom — piping the script into the
/// profile — leaves them in charge of it.
/// </remarks>
internal static class CliCompletion
{
    /// <summary>A completion script for PowerShell, for the commands and options this build has.</summary>
    internal const string PowerShellScript = """
        # Flint command completion. Add it to your profile with:
        #   flint completion powershell | Out-File -Append $PROFILE
        Register-ArgumentCompleter -Native -CommandName flint -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)

            $commands = 'version', 'doctor', 'update', 'completion'
            $options = '--address', '--port', '--receiver-port', '--pairing-code', '--media',
                '--mirror', '--mirror-width', '--browse', '--browser-port', '--services',
                '--json', '--version', '--help'

            $spoken = @($commandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { "$_" })
            $candidates = if ($spoken.Count -eq 0 -or ($spoken.Count -eq 1 -and $wordToComplete)) {
                $commands + $options
            }
            elseif ($spoken[0] -eq 'completion') { @('powershell') }
            elseif ($spoken[0] -in $commands) { @() }
            else { $options }

            $candidates |
                Where-Object { $_ -like "$wordToComplete*" } |
                ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
        }
        """;
}
