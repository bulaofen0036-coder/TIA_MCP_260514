using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace TiaMcpServer.ModelContextProtocol
{
    // Tool roster size. DEFAULT = lite: ~48 essentials instead of ~200, so a small /
    // non-expert model is not drowned in choices, hosts with a tool cap (Copilot 128,
    // Windsurf 100) can load the server at all, and every turn carries ~8k instead of
    // ~40k tokens of schema. Opt out per session with --profile full / TIA_MCP_PROFILE=full;
    // reach any individual non-lite tool without opting out via FindTools + CallTool.
    // All tools are static so no DI target is needed.
    public static partial class McpServer
    {
        // Explicit allowlist (tool Name, not method name). Kept explicit on purpose:
        // membership must not silently change when a [Lx] description prefix is edited.
        // = all [L0]/[L1] tools + the golden-path tools ServerInstructions/GetAuthoringGuide
        // tell the model to call (previously [L2] and thus missing from lite — a weak
        // model in lite was instructed to call ImportFromDocuments and couldn't see it).
        private static readonly HashSet<string> LiteToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            // L0 — the bridge to everything not listed here. Without these two, lite is a
            // dead end: the model cannot even discover that the other ~160 tools exist.
            "FindTools", "CallTool",
            // L0 — orientation / diagnostics
            "Bootstrap", "Doctor", "GetState", "GetAuthoringGuide",
            "GenerateAcceptanceReport", "GenerateErrorReport",
            "RunCapabilitySelfTest", "RunOnlineMonitoringSafetySelfTest",
            // L1 — session / project lifecycle
            "Connect", "Disconnect", "ListPortalProcessProjects", "EnsureOpennessUserGroup",
            "OpenProject", "AttachToOpenProject", "CreateProject", "SaveProject", "CloseProject",
            "GetProject", "GetProjectTree", "ValidateAutomationContext",
            // L1 — read / understand
            "GetSoftwareInfo", "GetSoftwareTree", "GetDevices", "DescribeBlockLogic",
            // L1 — build / import / compile
            "ScaffoldProject", "PlcBuildAndImport", "ImportBlock", "ImportType",
            "ImportPlcTagTable", "WritePlcSclSourceFile",
            "CompileSoftware", "CompileAndDiagnosePlc",
            // The HMI counterpart. Without it a lite session can generate Unified screens but
            // cannot read its own HMI compile errors, so it has to hand the project back to the
            // engineer to compile in the UI (#24).
            "CompileAndDiagnoseHmi",
            // L1 — hardware
            "AddDeviceWithFallback", "SearchHardwareCatalog", "ConnectDeviceNodesToProfinetSubnet",
            // Golden-path tools referenced by ServerInstructions / GetAuthoringGuide
            // (previously [L2]; without them the lite roster contradicts the instructions)
            "ImportFromDocuments", "GenerateBlocksFromExternalSource",
            // Batch SD import/export are the "PREFERRED on V21+" batch path in the same
            // instructions; tag tables and cross-references are what a model needs to read a
            // project it did not write.
            "ImportBlocksFromDocuments", "ExportBlocksAsDocuments",
            "GetPlcTagTables", "GetCrossReferences",
            "GetBlocks", "GetBlocksWithHierarchy", "GetBlockInfo",
            "ExportAsDocuments", "GoOffline",
        };

        public static IList<McpServerTool> GetLiteTools()
        {
            var tools = new List<McpServerTool>();
            foreach (var method in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null) continue;
                var name = attr.Name ?? method.Name;
                if (LiteToolNames.Contains(name))
                {
                    tools.Add(McpServerTool.Create(method));
                }
            }
            return tools;
        }

        // ---- Profile resolution -----------------------------------------------------------------
        // LITE IS THE DEFAULT. Measured on the V21 engine: the full roster is ~200 tools /
        // ~160 KB of JSON schema (~40k tokens) that every host re-sends to the model on EVERY
        // turn, before any work happens. Lite is ~48 tools / ~35 KB (~8k tokens).
        // It is also a hard compatibility wall, not just a cost: VS Code / Copilot refuse to
        // run agent mode above 128 tools and Windsurf is capped at 100, so the full roster
        // simply does not load there. Nothing is lost by defaulting to lite — FindTools /
        // CallTool (McpServer.ToolBridge.cs) reach every one of the other tools on demand.
        // Precedence: --profile flag > TIA_MCP_PROFILE env > lite.
        private static string? _profileOverride;

        /// <summary>Applies the CLI --profile flag. Wins over TIA_MCP_PROFILE. Call before building the host.</summary>
        public static void SetProfileOverride(string? profile)
        {
            _profileOverride = string.IsNullOrWhiteSpace(profile) ? null : profile!.Trim();
        }

        /// <summary>Resolved profile name, always lowercase: "lite" or "full".</summary>
        public static string ResolvedProfile()
        {
            string? p = _profileOverride;
            if (string.IsNullOrEmpty(p)) p = Environment.GetEnvironmentVariable("TIA_MCP_PROFILE");
            p = p?.Trim();
            if (string.IsNullOrEmpty(p)) return "lite";
            // Only "full" (and the historical "all") opts out; anything else — including a
            // typo — stays on the safe, host-compatible lite roster rather than silently
            // blowing past a host's tool cap.
            if (string.Equals(p, "full", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "all", StringComparison.OrdinalIgnoreCase)) return "full";
            return "lite";
        }

        public static bool IsLiteProfile()
        {
            return ResolvedProfile() == "lite";
        }
    }
}
