namespace KillerMCP;

internal static class ToolSafety
{
    private static readonly HashSet<string> ReadOnlyAppTools = new(StringComparer.Ordinal)
    {
        "killernotes_search", "killernotes_list", "killernotes_get", "killernotes_groups",
        "killernotes_tags", "killernotes_backlinks", "killernotes_links", "killernotes_history",
        "killernotes_stats", "killendar_agenda", "killerscan_local_network", "killerscan_list_locations",
        "killerscan_list_history", "killerscan_history_run",
        "killerscan_mac_vendor", "killerpdf_document_info", "killerpdf_search_text",
        "killerpdf_preflight", "killerpdf_accessibility", "killerpdf_pdfua", "killerpdf_verify_install",
        "killerpdf_print_production", "killerpdf_signatures", "killerpdf_layers", "killerpdf_portfolio",
        "killerpdf_comments", "killerpdf_navigation_audit", "killerpdf_measurements", "killerpdf_separations",
        "killerpdf_compare_structure", "killerpdf_list_attachments", "killerpdf_recognize_fields",
        "killerpdf_list_macros", "killerpdf_list_scanners",
        "killernotes_templates", "killernotes_trash_list", "killernotes_version_get", "killernotes_tag_definitions",
    };

    // KillerPDF tools that only write a new file or folder and never change their sources.
    private static readonly HashSet<string> AdditivePdfTools = new(StringComparer.Ordinal)
    {
        "killerpdf_optimize", "killerpdf_number_pages", "killerpdf_bates", "killerpdf_booklet", "killerpdf_nup",
        "killerpdf_auto_bookmarks", "killerpdf_scan_to_text", "killerpdf_reverse_pages", "killerpdf_fix_orientation",
        "killerpdf_straighten", "killerpdf_divide_spreads", "killerpdf_rasterize_region", "killerpdf_render_separations",
        "killerpdf_extract_attachments", "killerpdf_add_form_fields", "killerpdf_set_layer", "killerpdf_portfolio_view",
        "killerpdf_add_attachment", "killerpdf_edit_attachment", "killerpdf_repair_links", "killerpdf_table_of_contents",
        "killerpdf_page_labels", "killerpdf_initial_view", "killerpdf_redact", "killerpdf_protect", "killerpdf_export",
        "killerpdf_convert_office", "killerpdf_convert_book", "killerpdf_data_merge", "killerpdf_replace_text", "killerpdf_export_form_data",
        "killerpdf_import_form_data", "killerpdf_xfa_to_acroform", "killerpdf_flatten_forms", "killerpdf_run_macro",
        "killerpdf_sign", "killerpdf_scan", "killerpdf_images_to_pdf",
        "killerpdf_set_metadata", "killerpdf_set_bookmarks", "killerpdf_add_comment", "killerpdf_delete_comments", "killerpdf_crop",
    };

    private static readonly HashSet<string> ReadOnlyShellTools = new(StringComparer.Ordinal)
    {
        "killershell_search_files", "killershell_list_directory", "killershell_file_info",
        "killershell_read_text_file", "killershell_list_processes", "killershell_list_services",
        "killershell_read_event_log", "killershell_read_registry_key", "killershell_list_drives",
        "killershell_hash_file", "killershell_storage_report",
    };

    private static readonly HashSet<string> ReadOnlyShotTools = new(StringComparer.Ordinal)
    {
        "killershot_list_monitors", "killershot_list_windows", "killershot_recent", "killershot_settings",
        "killershot_read_text", "killershot_text_languages", "killershot_read_qr",
    };

    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "killernotes_update", "killernotes_set_group_color", "killernotes_set_title_color",
        "killerpdf_print", "killerbench_repair_execute",
        "decode_file_base64_local", "write_text_file_local",
        "killershot_recording_control", "killernotes_pin", "killernotes_unpin", "killernotes_version_restore",
        "killernotes_rename_group", "killernotes_rename_tag", "killernotes_set_tag_color",
        "killernotes_template_group", "killernotes_daily_group",
    };

    private static readonly HashSet<string> NetworkTools = new(StringComparer.Ordinal)
    {
        "search_gifs", "lookup_domain_dns", "lookup_domain_rdap", "lookup_cve",
        "list_killer_scripts", "killermcp_update_status",
    };

    private static readonly HashSet<string> ReadOnlyBenchTools = new(StringComparer.Ordinal)
    {
        "killerbench_code_lookup", "killerbench_diagnostic_scopes", "killerbench_check", "killerbench_scan",
        "killerbench_job", "killerbench_repair_actions", "killerbench_repair_preview",
        "killerbench_evidence", "killerbench_knowledge", "killerbench_review", "killerbench_catalog",
    };

    private static readonly HashSet<string> ReadOnlyCoreTools = new(StringComparer.Ordinal)
    {
        "text_statistics", "convert_case", "encode_base64", "decode_base64", "text_to_ascii_binary", "ascii_binary_to_text",
        "draw_ascii_text", "search_emoji", "arabic_to_roman", "roman_to_arabic", "text_to_nato_alphabet", "convert_integer_base",
        "convert_temperature", "calculate_percentage", "escape_html_entities", "unescape_html_entities", "markdown_to_html",
        "format_sql", "convert_date_time", "lookup_exchange_ndr", "lookup_group_policy", "lookup_http_status", "lookup_m365_sku",
        "lookup_port_protocol", "lookup_windows_error", "lookup_windows_event", "list_killer_modules", "list_killer_scripts",
        "search_powershell_cmdlets", "get_powershell_cmdlet", "build_powershell_command", "diff_text", "format_json", "minify_json",
        "diff_json", "convert_xml_json", "format_xml", "convert_json", "convert_yaml", "convert_toml", "format_yaml",
        "generate_meta_tags", "search_gifs", "get_browser_device_information_local", "get_browser_keycode_local",
        "get_browser_html_local", "get_browser_signature_local", "get_browser_camera_local", "describe_cron", "calculate_nd_exposure",
        "calculate_exposure_equivalence", "calculate_depth_of_field", "generate_svg_placeholder", "generate_qr_code", "calculate_chmod",
        "expand_ipv4_range", "calculate_ipv4_subnet", "generate_ipv6_ula", "json_to_csv", "parse_url", "parse_user_agent",
        "parse_phone_number", "convert_color", "lookup_mac_vendor", "lookup_domain_dns", "lookup_domain_rdap", "lookup_cve",
        "generate_spf_record", "generate_dmarc_record", "parse_email_headers", "test_regex", "encode_file_base64_local",
        "check_pdf_signatures_local", "otp_private", "generate_otp_secret_private", "generate_ulids", "generate_uuids",
        "generate_lorem_ipsum", "parse_jwt_private", "generate_rsa_keypair_private", "crypt_text_private", "hash_text_private",
        "hmac_private", "bcrypt_private", "bip39_private", "analyze_password_private", "generate_password_private", "evaluate_math",
        "list_film_stocks", "calculate_reciprocity", "list_film_development_options", "calculate_film_development", "killermcp_update_status",
    };

    private static readonly HashSet<string> ActiveScanTools = new(StringComparer.Ordinal)
    {
        "killerscan_scan_network", "killerscan_probe_host", "killerscan_ping", "killerscan_trace_route",
        "killerscan_diagnose_host", "killerscan_watch_hosts", "killerscan_speed_test",
    };

    private static readonly HashSet<string> AdditiveShotTools = new(StringComparer.Ordinal)
    {
        "killershot_capture_monitor", "killershot_capture_all", "killershot_capture_window", "killershot_capture_region",
        "killershot_record", "killershot_open_in_editor", "killershot_edit_image",
    };

    public static object For(string name)
    {
        bool readOnly = IsReadOnly(name);
        if (!readOnly && !DestructiveTools.Contains(name) && !IsKnownAdditive(name))
            throw new InvalidOperationException("Tool annotations are missing for " + name);
        bool openWorld = ActiveScanTools.Contains(name)
            || name is "open_browser_companion_local" or "killerpdf_print" or "killerpdf_ocr"
            or "killerpdf_scan_to_text" or "killerpdf_fix_orientation" or "killerpdf_run_macro" or "killerpdf_sign" or "killerpdf_scan"
            or "killerbench_check" or "killerbench_scan" or "killerbench_repair_preview" or "killerbench_repair_execute"
            || NetworkTools.Contains(name);
        return new
        {
            readOnlyHint = readOnly,
            destructiveHint = !readOnly && (DestructiveTools.Contains(name) || !IsKnownAdditive(name)),
            openWorldHint = openWorld,
        };
    }

    private static bool IsReadOnly(string name) => ReadOnlyShellTools.Contains(name) || ReadOnlyBenchTools.Contains(name)
        || ReadOnlyShotTools.Contains(name) || ReadOnlyAppTools.Contains(name) || ReadOnlyCoreTools.Contains(name);

    private static bool IsKnownAdditive(string name) =>
        // Recover archives finished repair journal records, so it writes local state without removing any.
        name is "killer_create_pdf" or "killendar_create_appointment" or "killerbench_repair_recover"
        or "killernotes_create" or "killernotes_create_group" or "killernotes_import_image"
        or "killernotes_export" or "killernotes_export_pdf" or "killendar_export_agenda_pdf"
        or "killendar_save_agenda_note" or "killerscan_export_report_pdf" or "killerscan_export_report_html"
        or "killerscan_save_report_note" or "killerbench_save_scan_note" or "killershell_export_directory_pdf"
        or "killershell_save_directory_note" or "killerpdf_save_pages_as_notes" or "killerpdf_save_text_as_note"
        or "killerpdf_save_report_as_note" or "killernotes_export_notes_pdf"
        or "killerpdf_merge" or "killerpdf_extract_pages" or "killerpdf_split"
        or "killerpdf_decrypt" or "killerpdf_render_pages" or "killerpdf_flatten"
        or "killerpdf_ocr" or "killerpdf_resave" or "killerpdf_benchmark_render"
        or "killerpdf_rotate_pages" or "killerpdf_delete_pages" or "killerpdf_move_pages"
        or "killerpdf_insert_blank_page" or "killerpdf_duplicate_page"
        or "killernotes_create_from_template" or "killernotes_today" or "killernotes_restore" or "killernotes_create_tag"
        or "open_browser_companion_local" || AdditivePdfTools.Contains(name) || ActiveScanTools.Contains(name) || AdditiveShotTools.Contains(name);
}
