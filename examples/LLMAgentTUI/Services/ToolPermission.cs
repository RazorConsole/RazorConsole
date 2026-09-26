// Copyright (c) RazorConsole. All rights reserved.

namespace LLMAgentTUI.Services;

public enum ToolPermission { AskForApproval, WorkspaceFiles, FullAccess }

public static class ToolPermissionPolicy
{
    public static bool CanAutoApprove(ToolPermission permission, string toolName)
        => permission switch
        {
            ToolPermission.WorkspaceFiles => toolName is "read" or "write" or "edit",
            ToolPermission.FullAccess => toolName is "read" or "write" or "edit" or "bash" or "powershell",
            _ => false,
        };
}
