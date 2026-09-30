namespace KillerMCP;

internal static class ToolSafety
{
    private static readonly HashSet<string> ReadOnlyAppTools = new(StringComparer.Ordinal)
    {
        "killernotes_search", "killernotes_list", "killernotes_get", "killernotes_groups",
        "killernotes_tags", "killernotes_backlinks", "killernotes_links", "killernotes_history",
        "killernotes_stats", "killendar_agenda", "killerscan_local_network",
        "killerscan_mac_vendor", "killerpdf_document_info", "killerpdf_search_text",
        "killerpdf_preflight", "killerpdf_accessibility",
    };

    private static readonly HashSet<string> ReadOnlyShellTools = new(StringComparer.Ordinal)
    {
        "killershell_search_files", "killershell_list_directory", "killershell_file_info",
        "killershell_read_text_file", "killershell_list_processes", "killershell_list_services",
        "killershell_read_event_log", "killershell_read_registry_key", "killershell_list_drives",
        "killershell_hash_file",
    };

    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "killernotes_update", "killernotes_set_group_color", "killernotes_set_title_color",
        "killerpdf_print",
    };

    private static readonly HashSet<string> NetworkTools = new(StringComparer.Ordinal)
    {
        "search_gifs", "lookup_domain_dns", "lookup_domain_rdap", "lookup_cve",
        "list_killer_scripts", "killermcp_update_status",
    };

    public static object For(string name)
    {
        bool readOnly = IsReadOnly(name);
        bool openWorld = name.StartsWith("killerscan_", StringComparison.Ordinal)
            && name is not ("killerscan_local_network" or "killerscan_mac_vendor")
            || name is "open_browser_companion_local" or "killerpdf_print" or "killerpdf_ocr"
            || NetworkTools.Contains(name);
        return new
        {
            readOnlyHint = readOnly,
            destructiveHint = !readOnly && (DestructiveTools.Contains(name) || !IsKnownAdditive(name)),
            openWorldHint = openWorld,
        };
    }

    private static bool IsReadOnly(string name)
    {
        if (name.StartsWith("killershell_", StringComparison.Ordinal))
            return ReadOnlyShellTools.Contains(name);
        if (name.StartsWith("killernotes_", StringComparison.Ordinal)
            || name.StartsWith("killendar_", StringComparison.Ordinal)
            || name.StartsWith("killerscan_", StringComparison.Ordinal)
            || name.StartsWith("killerpdf_", StringComparison.Ordinal)
            || name is "killer_create_pdf")
            return ReadOnlyAppTools.Contains(name);
        return name is not ("decode_file_base64_local" or "open_browser_companion_local");
    }

    private static bool IsKnownAdditive(string name) =>
        name is "decode_file_base64_local" or "killer_create_pdf" or "killendar_create_appointment"
        or "killernotes_create" or "killernotes_create_group" or "killernotes_import_image"
        or "killernotes_export" or "killernotes_export_pdf" or "killendar_export_agenda_pdf"
        or "killendar_save_agenda_note" or "killerscan_export_report_pdf"
        or "killerscan_save_report_note" or "killershell_export_directory_pdf"
        or "killershell_save_directory_note" or "killerpdf_save_pages_as_notes"
        or "killerpdf_merge" or "killerpdf_extract_pages" or "killerpdf_split"
        or "killerpdf_decrypt" or "killerpdf_render_pages" or "killerpdf_flatten"
        or "killerpdf_ocr" or "killerpdf_resave" or "killerpdf_benchmark_render"
        or "killerpdf_rotate_pages" or "killerpdf_delete_pages" or "killerpdf_move_pages"
        or "killerpdf_insert_blank_page" or "killerpdf_duplicate_page"
        or "open_browser_companion_local" ||
        name.StartsWith("killerscan_", StringComparison.Ordinal);
}
