using Flint.Core;

namespace Flint.Cli;

/// <summary>Validated command-line intent for the diagnostics runner.</summary>
internal sealed record CliOptions(
    bool ScanServices,
    bool ShowHelp,
    bool ShowVersion,
    bool Json,
    ManualProbeEndpoint? Endpoint,
    string? PairingCode,
    string? MediaPath,
    int ReceiverPort,
    bool Mirror = false,
    uint MirrorMaxWidth = 1920,
    string? BrowseUrl = null,
    int? BrowserPort = null)
{
    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out CliOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        options = null;
        error = null;

        var scanServices = false;
        var showHelp = false;
        var showVersion = false;
        var json = false;
        string? address = null;
        string? port = null;
        string? pairingCode = null;
        string? mediaPath = null;
        var receiverPort = 47855;
        var receiverPortWasExplicit = false;
        var mirror = false;
        var mirrorMaxWidth = 1920u;
        string? browseUrl = null;
        int? browserPort = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.Equals("--services", StringComparison.OrdinalIgnoreCase))
            {
                scanServices = true;
            }
            else if (argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (argument.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                showVersion = true;
            }
            else if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                // Machine-readable probe output. The human banner still goes to stderr so a
                // caller can redirect stdout straight into a parser without stripping it.
                json = true;
            }
            else if (argument.Equals("--address", StringComparison.OrdinalIgnoreCase))
            {
                if (address is not null || !TryReadValue(arguments, ref index, out address))
                {
                    error = "--address must be followed by one IP address and may appear only once.";
                    return false;
                }
            }
            else if (argument.Equals("--port", StringComparison.OrdinalIgnoreCase))
            {
                if (port is not null || !TryReadValue(arguments, ref index, out port))
                {
                    error = "--port must be followed by one port and may appear only once.";
                    return false;
                }
            }
            else if (argument.Equals("--pairing-code", StringComparison.OrdinalIgnoreCase))
            {
                if (pairingCode is not null || !TryReadValue(arguments, ref index, out pairingCode))
                {
                    error = "--pairing-code must be followed by the six-digit TV code.";
                    return false;
                }
            }
            else if (argument.Equals("--receiver-port", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(arguments, ref index, out var receiverPortText)
                    || !int.TryParse(receiverPortText, out receiverPort)
                    || receiverPort is < 1 or > 65535)
                {
                    error = "--receiver-port must be between 1 and 65535.";
                    return false;
                }

                receiverPortWasExplicit = true;
            }
            else if (argument.Equals("--media", StringComparison.OrdinalIgnoreCase))
            {
                if (mediaPath is not null || !TryReadValue(arguments, ref index, out mediaPath))
                {
                    error = "--media must be followed by one local media file and may appear only once.";
                    return false;
                }
            }
            else if (argument.Equals("--mirror", StringComparison.OrdinalIgnoreCase))
            {
                mirror = true;
            }
            else if (argument.Equals("--browser-port", StringComparison.OrdinalIgnoreCase))
            {
                if (browserPort is not null
                    || !TryReadValue(arguments, ref index, out var browserPortText)
                    || !int.TryParse(browserPortText, out var parsedBrowserPort)
                    || parsedBrowserPort is < 1 or > 65535)
                {
                    error = "--browser-port must be followed by one port between 1 and 65535.";
                    return false;
                }

                browserPort = parsedBrowserPort;
            }
            else if (argument.Equals("--browse", StringComparison.OrdinalIgnoreCase))
            {
                if (browseUrl is not null || !TryReadValue(arguments, ref index, out browseUrl))
                {
                    error = "--browse must be followed by one https:// address and may appear only once.";
                    return false;
                }
            }
            else if (argument.Equals("--mirror-width", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(arguments, ref index, out var widthText)
                    || !uint.TryParse(widthText, out mirrorMaxWidth)
                    || mirrorMaxWidth is < 320 or > 7680)
                {
                    error = "--mirror-width must be between 320 and 7680.";
                    return false;
                }
            }
            else
            {
                error = $"Unknown option '{argument}'.";
                return false;
            }
        }

        // Help and version answer immediately and ignore everything else on the line: someone
        // asking what this is should not first have to make the rest of their arguments valid.
        if (showHelp || showVersion)
        {
            options = new CliOptions(
                false, showHelp, showVersion, json, null, null, null, receiverPort);
            return true;
        }

        if (scanServices && (address is not null || port is not null || pairingCode is not null || mediaPath is not null))
        {
            error = "--services cannot be combined with --address or --port.";
            return false;
        }

        if (port is not null && address is null)
        {
            error = "--port requires --address.";
            return false;
        }

        ManualProbeEndpoint? endpoint = null;
        if (address is not null
            && !ManualProbeEndpoint.TryParse(address, port, out endpoint, out error))
        {
            return false;
        }

        if (pairingCode is not null && (pairingCode.Length != 6 || !pairingCode.All(char.IsDigit)))
        {
            error = "--pairing-code must contain six digits.";
            return false;
        }

        // A paired command targets the receiver protocol, so its unqualified --port belongs to
        // that endpoint. Capability probing must still scan the bounded ADB range independently;
        // otherwise `--port 47855` probes the receiver as though it were adbd, prints a false ADB
        // failure, and then successfully connects to the very same receiver on the default port.
        // Pure diagnostic commands retain the established meaning: --port is one explicit ADB
        // port. --receiver-port remains as the unambiguous spelling for scripts.
        if (pairingCode is not null && endpoint?.Port is { } pairedPort)
        {
            if (receiverPortWasExplicit)
            {
                error = "Use either --port or --receiver-port for a paired receiver, not both.";
                return false;
            }

            receiverPort = pairedPort;
            endpoint = endpoint with { Port = null };
        }

        if (mediaPath is not null && (endpoint is null || pairingCode is null))
        {
            error = "--media requires --address and --pairing-code.";
            return false;
        }

        if (mirror && (endpoint is null || pairingCode is null))
        {
            error = "--mirror requires --address and --pairing-code.";
            return false;
        }

        if (browseUrl is not null && (endpoint is null || pairingCode is null))
        {
            error = "--browse requires --address and --pairing-code.";
            return false;
        }

        // The browser session is separately authenticated and pinned, so only an https address can
        // be opened. Refusing here rather than at the receiver keeps the failure next to the typo.
        if (browseUrl is not null
            && !browseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            error = "--browse only accepts an https:// address.";
            return false;
        }

        // Each of these owns the television's screen. Running two would leave whichever finished
        // second showing over the other, with no way to tell from the host which won.
        if (browseUrl is not null && (mirror || mediaPath is not null))
        {
            error = "--browse cannot be combined with --mirror or --media.";
            return false;
        }

        // Only meaningful alongside --browse, and a port given without one is a typo worth naming
        // rather than silently ignoring.
        if (browserPort is not null && browseUrl is null)
        {
            error = "--browser-port only applies with --browse.";
            return false;
        }

        // A file and a live screen are two different things to put on one surface, and doing both
        // would leave whichever finished second showing over the other.
        if (mirror && mediaPath is not null)
        {
            error = "--mirror cannot be combined with --media.";
            return false;
        }

        options = new CliOptions(
            scanServices,
            false,
            false,
            json,
            endpoint,
            pairingCode,
            mediaPath,
            receiverPort,
            mirror,
            mirrorMaxWidth,
            browseUrl,
            browserPort);
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value)
    {
        value = null;
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = arguments[++index];
        return true;
    }
}
