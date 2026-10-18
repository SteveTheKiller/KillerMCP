using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP.Runtime;

// The KillerPDF 1.9 document commands: page tools, Document tools reports and edits, redaction,
// protection, export, forms, macros, signing and scanning. Each tool appears only when the
// installed KillerPDF lists its command in --help.
public static partial class KillerPdfAdapter
{
    private static readonly string[] OfficeOutputs = [".docx", ".xlsx", ".pptx", ".html", ".md", ".txt", ".json"];
    private static readonly string[] FormDataOutputs = [".fdf", ".xfdf", ".json"];
    private static readonly string[] OfficeInputs = [".docx", ".xlsx", ".doc", ".xls"];

    private static IEnumerable<Operation> DocumentOperations()
    {
        // ---- Pages and output -----------------------------------------------------------------
        yield return FileOperation("killerpdf_optimize", "--optimize <in.pdf>", "Reduce a local PDF's file size into a new PDF, optionally removing named content and downsampling images drawn above a resolution. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["strip"] = Text(256, "^[a-z]+(?:,[a-z]+)*$", "Comma-separated content to remove: metadata, attachments, javascript, comments, thumbnails, bookmarks, forms, xfa, openaction, layers"), ["imageDpi"] = Integer(36, 1200), ["jpegQuality"] = Integer(1, 100) }, "path", "output"),
            (input, output) => Options(["--optimize", String(input, "path"), output], input, ("strip", "--strip"), ("imageDpi", "--images"), ("jpegQuality", "--jpeg-quality")), 300);
        yield return FileOperation("killerpdf_number_pages", "--number-pages <in.pdf>", "Add page numbers or a header or footer template to a local PDF into a new PDF. Template tokens: {page} {pages} {label} {filename} {title} {author} {date}. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["template"] = Text(256), ["pages"] = PageRange(), ["edge"] = Choice("header", "footer"), ["align"] = Choice("left", "center", "right"), ["numberFormat"] = Choice("decimal", "upper-roman", "lower-roman", "upper-letters", "lower-letters"), ["fontSize"] = Range(4, 144) }, "path", "output"),
            (input, output) => Options(["--number-pages", String(input, "path"), output], input, ("template", "--template"), ("pages", "--pages"), ("edge", "--edge"), ("align", "--align"), ("numberFormat", "--number-format"), ("fontSize", "--font-size")));
        yield return new Operation("--bates <outDir>", OutputKind.Directory, new RuntimeTool("killerpdf_bates", "Stamp one continuous Bates number sequence across local PDFs, writing the stamped copies into a new folder. Never changes the sources.",
            Schema(new JsonObject { ["inputs"] = ListOf(PdfPath(), 1, 50), ["outputFolder"] = NewFolder(), ["start"] = Integer(0, 999999999), ["digits"] = Integer(1, 12), ["prefix"] = Text(64), ["suffix"] = Text(64), ["edge"] = Choice("header", "footer"), ["align"] = Choice("left", "center", "right"), ["fontSize"] = Range(4, 144) }, "inputs", "outputFolder")),
            (input, output) => Options(["--bates", output, .. Strings(input, "inputs")], input, ("start", "--start"), ("digits", "--digits"), ("prefix", "--prefix"), ("suffix", "--suffix"), ("edge", "--edge"), ("align", "--align"), ("fontSize", "--font-size")), 300)
            with { InputExtensions = [], ExtraFiles = ["inputs"] };
        yield return FileOperation("killerpdf_booklet", "--booklet <in.pdf>", "Impose a local PDF as a two-up saddle-stitch booklet in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["sheet"] = Sheet(), ["margin"] = Points(720), ["gutter"] = Points(720), ["creep"] = Points(72), ["binding"] = Choice("long", "short"), ["marks"] = Boolean(false) }, "path", "output"),
            (input, output) => Options(["--booklet", String(input, "path"), output], input, ("sheet", "--sheet"), ("margin", "--margin"), ("gutter", "--gutter"), ("creep", "--creep"), ("binding", "--binding"), ("marks", "--marks")));
        yield return FileOperation("killerpdf_nup", "--nup <in.pdf>", "Place several pages of a local PDF on each sheet of a new PDF, such as a 2x2 grid. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["grid"] = Text(5, "^\\d{1,2}x\\d{1,2}$", "Columns by rows, such as 2x2"), ["sheet"] = Sheet(), ["margin"] = Points(720), ["gutter"] = Points(720), ["marks"] = Boolean(false) }, "path", "output", "grid"),
            (input, output) => Options(["--nup", String(input, "path"), output], input, ("grid", "--grid"), ("sheet", "--sheet"), ("margin", "--margin"), ("gutter", "--gutter"), ("marks", "--marks")));
        yield return FileOperation("killerpdf_auto_bookmarks", "--auto-bookmarks <in.pdf>", "Build bookmarks from the detected headings of a local PDF into a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["minSize"] = Range(0, 200), ["depth"] = Integer(1, 6), ["pattern"] = Text(256, null, "Regular expression a heading must match") }, "path", "output"),
            (input, output) => Options(["--auto-bookmarks", String(input, "path"), output], input, ("minSize", "--min-size"), ("depth", "--depth"), ("pattern", "--pattern")));
        yield return FileOperation("killerpdf_scan_to_text", "--scan-to-text <in.pdf>", "Replace the words on scanned pages of a local PDF with real text in the closest matching typeface, in a new PDF. reviewFolder receives each converted word as scanned beside it as text (p<page>-w<word>.png, least certain first in review.json); keepScanned lists page:word ids from a review to keep as scanned. A language model may be downloaded on first use. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["language"] = Language(), ["reviewFolder"] = NewFolder(), ["keepScanned"] = ListOf(Text(16, "^\\d+:\\d+$", "page:word, such as 3:17"), 1, 10000) }, "path", "output"),
            (input, output) => KeepScanned(Options(["--scan-to-text", String(input, "path"), output, "--lang", OptionalString(input, "language", "eng")], input, ("reviewFolder", "--review")), input, output), 900)
            with { NewFolders = ["reviewFolder"] };
        yield return FileOperation("killerpdf_reverse_pages", "--reverse-pages <in.pdf>", "Reverse the order of all or the selected pages of a local PDF into a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["pages"] = PageRange() }, "path", "output"),
            (input, output) => Options(["--reverse-pages", String(input, "path"), output], input, ("pages", "--pages")));
        yield return FileOperation("killerpdf_fix_orientation", "--fix-orientation <in.pdf>", "Turn the pages of a local PDF to read upright, reading scans with OCR, into a new PDF. A language model may be downloaded on first use. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["pages"] = PageRange(), ["language"] = Language() }, "path", "output"),
            (input, output) => Options(["--fix-orientation", String(input, "path"), output, "--lang", OptionalString(input, "language", "eng")], input, ("pages", "--pages")), 600);
        yield return FileOperation("killerpdf_straighten", "--straighten <in.pdf>", "Level tilted scanned pages that have no text layer in a local PDF, into a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["pages"] = PageRange() }, "path", "output"),
            (input, output) => Options(["--straighten", String(input, "path"), output], input, ("pages", "--pages")), 300);
        yield return FileOperation("killerpdf_divide_spreads", "--divide-spreads <in.pdf>", "Split scanned two-page spreads of a local PDF into separate pages in reading order, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["pages"] = PageRange(), ["direction"] = Choice("vertical", "horizontal"), ["splitAt"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.1, ["maximum"] = 0.9, ["description"] = "Where the split falls across the page, 0.5 for the middle" } }, "path", "output"),
            (input, output) => Options(["--divide-spreads", String(input, "path"), output], input, ("pages", "--pages"), ("direction", "--direction"), ("splitAt", "--position")));
        yield return FileOperation("killerpdf_rasterize_region", "--rasterize-region <in.pdf>", "Turn one box of a page of a local PDF into an image, in a new PDF. The box is left,bottom,right,top in points on the unrotated page. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["page"] = Integer(1, 100000), ["box"] = Text(80, "^-?\\d+(?:\\.\\d+)?(?:,-?\\d+(?:\\.\\d+)?){3}$", "left,bottom,right,top in points"), ["dpi"] = Integer(36, 1200, 300) }, "path", "output", "page", "box"),
            (input, output) => ["--rasterize-region", String(input, "path"), Number(input, "page"), String(input, "box"), output, "--dpi", Number(input, "dpi", 300)]);

        // ---- Document tools reports -------------------------------------------------------------
        yield return ReportOperation("killerpdf_print_production", "--print-production <in.pdf>", "Report the page boxes, inks and output intents of a local PDF without changing it.");
        yield return ReportOperation("killerpdf_signatures", "--signatures <in.pdf>", "Report each digital signature in a local PDF: signer, signing time, certificate trust, timestamp, and whether the document changed after signing. Changes nothing.");
        yield return ReportOperation("killerpdf_layers", "--layers <in.pdf>", "List the layers of a local PDF with their visibility and locks without changing it.");
        yield return ReportOperation("killerpdf_portfolio", "--portfolio <in.pdf>", "Report a local PDF portfolio's presentation, fields and folders without changing it.");
        yield return ReportOperation("killerpdf_comments", "--comments <in.pdf>", "List the review comments in a local PDF and their replies without changing it.");
        yield return ReportOperation("killerpdf_navigation_audit", "--navigation-audit <in.pdf>", "Check the links, bookmarks and destinations of a local PDF for unsafe or broken targets without changing it.");
        yield return ReportOperation("killerpdf_measurements", "--measurements <in.pdf>", "Report the distance, perimeter, area and angle measurements saved in a local PDF without changing it.");
        yield return ReportOperation("killerpdf_separations", "--separations <in.pdf>", "List the process and spot inks used on each page of a local PDF without changing it.");
        yield return DirectoryOperation("killerpdf_render_separations", "--separations <in.pdf>", "Write each page's ink plates of a local PDF as images into a new folder. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder(), ["pages"] = PageRange(), ["dpi"] = Integer(36, 1200, 150) }, "path", "outputFolder"),
            (input, output) => Options(["--separations", String(input, "path"), output, "--dpi", Number(input, "dpi", 150)], input, ("pages", "--pages")), 600);
        yield return new Operation("--compare-structure <original.pdf>", OutputKind.Report, new RuntimeTool("killerpdf_compare_structure", "Compare the content, resources and page geometry of two local PDFs and report what changed. Changes neither file.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["changed"] = PdfPath() }, "path", "changed")),
            (input, _) => ["--compare-structure", String(input, "path"), String(input, "changed"), "--json"], 120)
            with { ExtraFiles = ["changed"], SuccessCodes = [0, 3] };
        yield return ReportOperation("killerpdf_list_attachments", "--attachments <in.pdf>", "List the files embedded in a local PDF without changing it.");
        yield return DirectoryOperation("killerpdf_extract_attachments", "--attachments <in.pdf>", "Extract the files embedded in a local PDF into a new folder. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder() }, "path", "outputFolder"),
            (input, output) => ["--attachments", String(input, "path"), output]);
        yield return ReportOperation("killerpdf_recognize_fields", "--recognize-fields <in.pdf>", "Find the places on a local PDF that look like form fields, such as text boxes and check boxes, without changing it.");

        // ---- Document tools edits ---------------------------------------------------------------
        yield return FileOperation("killerpdf_add_form_fields", "--recognize-fields <in.pdf>", "Add the text fields and check boxes recognized on a local PDF as fillable form fields, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf() }, "path", "output"),
            (input, output) => ["--recognize-fields", String(input, "path"), output]);
        yield return FileOperation("killerpdf_set_layer", "--set-layer <in.pdf>", "Rename a layer of a local PDF or change its saved visibility or lock, in a new PDF. Name the layer, or #object for its object number. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["layer"] = Positional(256), ["name"] = Text(256), ["visible"] = Boolean(true), ["locked"] = Boolean(false) }, "path", "output", "layer"),
            (input, output) => OnOff(Options(["--set-layer", String(input, "path"), output, String(input, "layer")], input, ("name", "--name")), input, ("visible", "--visible"), ("locked", "--locked")));
        yield return FileOperation("killerpdf_portfolio_view", "--portfolio-view <in.pdf>", "Set how a local PDF portfolio opens, as details, tiles or hidden with an initial attachment, or clear its portfolio settings, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["view"] = Choice("details", "tile", "hidden"), ["initial"] = Text(256), ["clear"] = Boolean(false) }, "path", "output"),
            (input, output) => Flag(input, "clear") ? ["--portfolio-view", String(input, "path"), output, "--clear"]
                : Options(["--portfolio-view", String(input, "path"), output], input, ("view", "--view"), ("initial", "--initial")));
        yield return FileOperation("killerpdf_add_attachment", "--add-attachment <in.pdf>", "Embed local files in a local PDF, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["files"] = ListOf(FilePath("Absolute path to a local file to embed"), 1, 16), ["description"] = Text(1024), ["relationship"] = Relationship() }, "path", "output", "files"),
            (input, output) => Options(["--add-attachment", String(input, "path"), output, .. Strings(input, "files")], input, ("description", "--description"), ("relationship", "--relationship")))
            with { ExtraFiles = ["files"] };
        yield return FileOperation("killerpdf_edit_attachment", "--edit-attachment <in.pdf>", "Rename, describe, retype or remove one embedded file of a local PDF, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["name"] = Positional(256), ["rename"] = Text(256), ["description"] = Text(1024), ["mime"] = Text(128, "^[\\w.+-]+/[\\w.+-]+$", "MIME type such as text/csv"), ["relationship"] = Relationship(), ["remove"] = Boolean(false) }, "path", "output", "name"),
            (input, output) => Options(["--edit-attachment", String(input, "path"), output, String(input, "name")], input, ("rename", "--rename"), ("description", "--description"), ("mime", "--mime"), ("relationship", "--relationship"), ("remove", "--remove")));
        yield return FileOperation("killerpdf_repair_links", "--repair-links <in.pdf>", "Remove unsafe links, broken links, or both (the default) from a local PDF, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["unsafe"] = Boolean(false), ["unresolved"] = Boolean(false) }, "path", "output"),
            (input, output) => Options(["--repair-links", String(input, "path"), output], input, ("unsafe", "--unsafe"), ("unresolved", "--unresolved")));
        yield return FileOperation("killerpdf_table_of_contents", "--toc <in.pdf>", "Insert clickable table-of-contents pages built from the bookmarks of a local PDF, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["title"] = Text(256), ["depth"] = Integer(1, 6) }, "path", "output"),
            (input, output) => Options(["--toc", String(input, "path"), output], input, ("title", "--title"), ("depth", "--depth")));
        yield return FileOperation("killerpdf_page_labels", "--page-labels <in.pdf>", "Set the page-label ranges of a local PDF, such as 1:lower-roman,5:decimal or 40:decimal:A-:1, in a new PDF. Styles: decimal, upper-roman, lower-roman, upper-letters, lower-letters, none. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["ranges"] = Text(1024, "^\\d+:[a-z-]+(?::[^:,]*(?::\\d+)?)?(?:,\\d+:[a-z-]+(?::[^:,]*(?::\\d+)?)?)*$", "page:style[:prefix[:start]], comma separated") }, "path", "output", "ranges"),
            (input, output) => ["--page-labels", String(input, "path"), output, String(input, "ranges")]);
        yield return FileOperation("killerpdf_initial_view", "--initial-view <in.pdf>", "Set how a local PDF opens: page, zoom, layout, side panel and window options, in a new PDF. Never changes the source.",
            Schema(new JsonObject
            {
                ["path"] = PdfPath(), ["output"] = NewPdf(), ["page"] = Integer(1, 100000),
                ["zoom"] = Text(10, "^(?:fit-page|fit-width|fit-height|actual|\\d{1,4})$", "fit-page, fit-width, fit-height, actual, or a percent"),
                ["layout"] = Choice("default", "single", "one-column", "two-column-left", "two-column-right", "two-page-left", "two-page-right"),
                ["panel"] = Choice("default", "none", "outlines", "thumbnails", "layers", "attachments", "full-screen"),
                ["hideToolbar"] = Boolean(false), ["hideMenu"] = Boolean(false), ["hideWindowUi"] = Boolean(false),
                ["fitWindow"] = Boolean(false), ["centerWindow"] = Boolean(false), ["showTitle"] = Boolean(false),
            }, "path", "output"),
            (input, output) => OnOff(Options(["--initial-view", String(input, "path"), output], input, ("page", "--page"), ("zoom", "--zoom"), ("layout", "--layout"), ("panel", "--panel")),
                input, ("hideToolbar", "--hide-toolbar"), ("hideMenu", "--hide-menu"), ("hideWindowUi", "--hide-window-ui"), ("fitWindow", "--fit-window"), ("centerWindow", "--center-window"), ("showTitle", "--show-title")));

        // ---- Redaction, protection, export and editing ------------------------------------------
        yield return FileOperation("killerpdf_redact", "--redact <in.pdf>", "Permanently remove text, images and drawings under matches of phrases, a regular expression, email addresses, phone, Social Security, card and IBAN numbers, whole pages, or the redaction marks saved in a local PDF, with verification, in a new PDF. Pages that can only be redacted as images are skipped unless rasterize is true. Never changes the source.",
            Schema(new JsonObject
            {
                ["path"] = PdfPath(), ["output"] = NewPdf(), ["text"] = Text(512), ["terms"] = ListOf(Text(512), 1, 500), ["regex"] = Text(512),
                ["find"] = ListOf(Choice("email", "phone", "ssn", "card", "iban"), 1, 5), ["wholePages"] = PageRange(), ["marked"] = Boolean(false),
                ["matchCase"] = Boolean(false), ["fill"] = Text(32, "^(?:#?[0-9A-Fa-f]{6}|[A-Za-z]+)$", "Fill color such as #000000 or white"),
                ["overlay"] = Text(256, null, "Text drawn in each redacted area, such as a redaction code"), ["rasterize"] = Boolean(false),
                ["sanitize"] = Text(128, "^(?:all|[a-z]+(?:,[a-z]+)*)$", "all, or any of comments, attachments, forms, layers, scripts, links, bookmarks"),
            }, "path", "output"),
            (input, output) => Terms(Options(["--redact", String(input, "path"), output], input, ("text", "--text"), ("regex", "--regex"), ("wholePages", "--whole-pages"), ("marked", "--marked"), ("matchCase", "--match-case"), ("fill", "--fill"), ("overlay", "--overlay"), ("rasterize", "--rasterize"), ("sanitize", "--sanitize"))
                .Concat(Joined(input, "find", "--find")).ToList(), input, output), 600);
        yield return FileOperation("killerpdf_protect", "--protect <in.pdf>", "Write an AES-256 encrypted copy of a local PDF protected by passwords with print, copy and edit permissions, or for certificate recipients. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["userPassword"] = Password(), ["ownerPassword"] = Password(), ["allow"] = ListOf(Choice("print", "copy", "edit"), 0, 3), ["recipients"] = ListOf(FilePath("Absolute path to a recipient certificate (.cer)", "^[^,]+$"), 1, 16), ["invalidateSignatures"] = Boolean(false) }, "path", "output"),
            (input, output) => Options(["--protect", String(input, "path"), output], input, ("userPassword", "--user-password"), ("ownerPassword", "--owner-password"), ("invalidateSignatures", "--invalidate-signatures"))
                .Concat(Joined(input, "allow", "--allow")).Concat(Joined(input, "recipients", "--recipients")).ToList())
            with { ExtraFiles = ["recipients"] };
        yield return FileOperation("killerpdf_export", "--export <in.pdf>", "Export a local PDF to Word, Excel, PowerPoint, HTML, Markdown, plain text or JSON, chosen by the output extension. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewFile("Absolute path for a new .docx, .xlsx, .pptx, .html, .md, .txt or .json file"), ["pages"] = PageRange() }, "path", "output"),
            (input, output) => Options(["--export", String(input, "path"), output], input, ("pages", "--pages")), 600)
            with { OutputExtensions = OfficeOutputs };
        yield return FileOperation("killerpdf_convert_office", "--convert-office <in.docx", "Convert a local Word or Excel file to a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = FilePath("Absolute path to a local .docx, .xlsx, .doc or .xls file"), ["output"] = NewPdf() }, "path", "output"),
            (input, output) => ["--convert-office", String(input, "path"), output], 300)
            with { InputExtensions = OfficeInputs };
        yield return DirectoryOperation("killerpdf_data_merge", "--data-merge <template.pdf>", "Fill one copy of a local PDF form per record of a CSV, JSON or XLSX file, writing the copies into a new folder. Never changes the sources.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["data"] = FilePath("Absolute path to a local .csv, .json or .xlsx file"), ["outputFolder"] = NewFolder(), ["name"] = Text(256, null, "File name template for each copy"), ["flatten"] = Boolean(false) }, "path", "data", "outputFolder"),
            (input, output) => Options(["--data-merge", String(input, "path"), String(input, "data"), output], input, ("name", "--name"), ("flatten", "--flatten")), 600)
            with { ExtraFiles = ["data"] };
        yield return FileOperation("killerpdf_replace_text", "--replace-text <in.pdf>", "Rewrite the lines of a local PDF that contain a phrase, replacing it in the line's own font, in a new PDF. Lines that cannot be rewritten are reported. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["find"] = Positional(512), ["replace"] = Positional(512, 0), ["matchCase"] = Boolean(false), ["wholeWord"] = Boolean(false) }, "path", "output", "find", "replace"),
            (input, output) => Options(["--replace-text", String(input, "path"), output, String(input, "find"), String(input, "replace")], input, ("matchCase", "--match-case"), ("wholeWord", "--whole-word")));
        yield return FileOperation("killerpdf_export_form_data", "--export-form-data <in.pdf>", "Export the form field values of a local PDF to a new FDF, XFDF or JSON file, chosen by the output extension. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewFile("Absolute path for a new .fdf, .xfdf or .json file"), ["noComments"] = Boolean(false) }, "path", "output"),
            (input, output) => Options(["--export-form-data", String(input, "path"), output], input, ("noComments", "--no-comments")))
            with { OutputExtensions = FormDataOutputs };
        yield return FileOperation("killerpdf_import_form_data", "--import-form-data <in.pdf>", "Fill the form fields of a local PDF from a local FDF or XFDF file, in a new PDF. Never changes the sources.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["data"] = FilePath("Absolute path to a local .fdf or .xfdf file"), ["output"] = NewPdf(), ["flatten"] = Boolean(false) }, "path", "data", "output"),
            (input, output) => Options(["--import-form-data", String(input, "path"), String(input, "data"), output], input, ("flatten", "--flatten")))
            with { ExtraFiles = ["data"] };
        yield return FileOperation("killerpdf_xfa_to_acroform", "--xfa-to-acroform <in.pdf>", "Convert the XFA form of a local PDF to standard form fields, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["flatten"] = Boolean(false) }, "path", "output"),
            (input, output) => Options(["--xfa-to-acroform", String(input, "path"), output], input, ("flatten", "--flatten")));
        yield return FileOperation("killerpdf_flatten_forms", "--flatten-forms <in.pdf>", "Turn the form fields of a local PDF into page content while the rest stays text and vectors, in a new PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf() }, "path", "output"),
            (input, output) => ["--flatten-forms", String(input, "path"), output]);

        // ---- Macros, signing and scanning -------------------------------------------------------
        yield return FileOperation("killerpdf_run_macro", "--macro <in.pdf>", "Run a KillerPDF macro on a local PDF into a new PDF: a saved macro by name, an exported macro file, or starter:archival, starter:sharing, starter:scanning or starter:privacy. Redact steps remove text and terms and the saved redaction marks. Never changes the source.",
            Schema(new JsonObject
            {
                ["path"] = PdfPath(), ["output"] = NewPdf(),
                ["macro"] = Text(128, "^(?:starter:(?:archival|sharing|scanning|privacy)|(?!--)[^\\\\/:*?\"<>|]+)$", "A saved macro's name, or starter:archival, starter:sharing, starter:scanning or starter:privacy"),
                ["macroFile"] = FilePath("Absolute path to an exported macro .json file"),
                ["language"] = Language(), ["text"] = Text(512), ["terms"] = ListOf(Text(512), 1, 500), ["rasterize"] = Boolean(false),
            }, "path", "output"),
            (input, output) => Terms(Options(["--macro", String(input, "path"), input.TryGetProperty("macroFile", out var file) ? file.GetString()! : OptionalString(input, "macro", "starter:sharing"), output, "--lang", OptionalString(input, "language", "eng")],
                input, ("text", "--text"), ("rasterize", "--rasterize")), input, output), 900)
            with { ExtraFiles = ["macroFile"] };
        yield return Action("killerpdf_list_macros", "--macro <in.pdf>", "List the saved KillerPDF macros and the starter macros with their steps.",
            Schema(new JsonObject()), _ => ["--macro", "--list"]) with { InputExtensions = [] };
        yield return FileOperation("killerpdf_sign", "--sign <in.pdf>", "Digitally sign a local PDF into a new PDF with a .pfx certificate file or a certificate from the Windows personal store, optionally with a trusted timestamp from a timestamp server and a visible signature. Never changes the source.",
            Schema(new JsonObject
            {
                ["path"] = PdfPath(), ["output"] = NewPdf(), ["pfx"] = FilePath("Absolute path to a .pfx or .p12 certificate file"), ["pfxPassword"] = Password(),
                ["certificate"] = Text(256, null, "Thumbprint or name of a signing certificate in the Windows personal store"),
                ["reason"] = Text(256), ["location"] = Text(256), ["contact"] = Text(256), ["timestamp"] = Boolean(false),
                ["timestampServer"] = Text(512, "^https?://\\S+$", "RFC 3161 timestamp server address"),
                ["appearancePage"] = Integer(1, 100000), ["appearanceCorner"] = Choice("bottom-left", "bottom-right", "top-left", "top-right"),
                ["appearanceText"] = Text(1024, null, "Visible signature text; {name}, {date}, {reason} and {location} are filled in"),
                ["width"] = Range(72, 14400), ["height"] = Range(36, 14400), ["fontSize"] = Range(6, 72),
            }, "path", "output"),
            (input, output) => SignArguments(input, output), 120)
            with { ExtraFiles = ["pfx"] };
        yield return new Operation("--scan <out.pdf>", OutputKind.File, new RuntimeTool("killerpdf_scan", "Scan pages through a TWAIN scanner into a new local PDF. Uses the named scanner, or the last one used, or the first found.",
            Schema(new JsonObject { ["output"] = NewPdf(), ["scanner"] = Text(256), ["showSettings"] = Boolean(false) }, "output")),
            (input, output) => Options(["--scan", output], input, ("scanner", "--scanner"), ("showSettings", "--show-settings")), 900)
            with { InputExtensions = [] };
        yield return Action("killerpdf_list_scanners", "--scan <out.pdf>", "List the TWAIN scanners KillerPDF can scan with.",
            Schema(new JsonObject()), _ => ["--scan", "--list"], 60) with { InputExtensions = [] };
        yield return new Operation("--merge <out.pdf>", OutputKind.File, new RuntimeTool("killerpdf_images_to_pdf", "Turn up to 50 local images, such as PNG or JPEG screenshots from KillerShot or TIFF scans, into one new PDF, one image per page in the order given. Never changes the images.",
            Schema(new JsonObject { ["images"] = ListOf(FilePath("Absolute path to a local .png, .jpg, .jpeg, .tif, .tiff, .bmp or .gif image", "\\.(?:[Pp][Nn][Gg]|[Jj][Pp][Ee]?[Gg]|[Tt][Ii][Ff][Ff]?|[Bb][Mm][Pp]|[Gg][Ii][Ff])$"), 1, 50), ["output"] = NewPdf() }, "images", "output")),
            (input, output) => ["--merge", output, .. Strings(input, "images")], 300)
            with { InputExtensions = [], ExtraFiles = ["images"] };
    }

    private static List<string> SignArguments(JsonElement input, string output)
    {
        var args = Options(["--sign", String(input, "path"), output], input, ("pfx", "--pfx"), ("pfxPassword", "--pfx-password"), ("certificate", "--cert"),
            ("reason", "--reason"), ("location", "--location"), ("contact", "--contact"), ("timestamp", "--timestamp"), ("timestampServer", "--timestamp-server"),
            ("appearanceText", "--appearance-text"), ("width", "--width"), ("height", "--height"), ("fontSize", "--font-size"));
        if (input.TryGetProperty("appearancePage", out var page))
            args.AddRange(["--appearance", page.GetRawText() + (input.TryGetProperty("appearanceCorner", out var corner) ? ":" + corner.GetString() : string.Empty)]);
        return args;
    }

    // Adds "--flag value" for each present text or number, and "--flag" alone for each true boolean.
    private static List<string> Options(List<string> args, JsonElement input, params (string Property, string Flag)[] options)
    {
        foreach (var (property, flag) in options)
        {
            if (!input.TryGetProperty(property, out var value)) continue;
            switch (value.ValueKind)
            {
                case JsonValueKind.String when !string.IsNullOrEmpty(value.GetString()): args.AddRange([flag, value.GetString()!]); break;
                case JsonValueKind.Number: args.AddRange([flag, value.GetRawText()]); break;
                case JsonValueKind.True: args.Add(flag); break;
            }
        }
        return args;
    }

    // Adds "--flag on" or "--flag off" for each present boolean.
    private static List<string> OnOff(List<string> args, JsonElement input, params (string Property, string Flag)[] options)
    {
        foreach (var (property, flag) in options)
            if (input.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                args.AddRange([flag, value.ValueKind == JsonValueKind.True ? "on" : "off"]);
        return args;
    }

    private static IEnumerable<string> Joined(JsonElement input, string property, string flag) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0
            ? [flag, string.Join(",", value.EnumerateArray().Select(item => item.GetString()))] : [];

    // KillerPDF reads a list of phrases from a file, written beside the staged output.
    private static List<string> Terms(List<string> args, JsonElement input, string output)
    {
        if (!input.TryGetProperty("terms", out var terms) || terms.ValueKind != JsonValueKind.Array || terms.GetArrayLength() == 0) return args;
        var file = Path.Combine(Path.GetDirectoryName(output)!, "terms.txt");
        File.WriteAllLines(file, terms.EnumerateArray().Select(item => item.GetString()!.Replace('\r', ' ').Replace('\n', ' ')));
        args.AddRange(["--terms", file]);
        return args;
    }

    // The words a review asked to keep as scanned, written beside the staged output.
    private static List<string> KeepScanned(List<string> args, JsonElement input, string output)
    {
        if (!input.TryGetProperty("keepScanned", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0) return args;
        var file = Path.Combine(Path.GetDirectoryName(output)!, "keep-scanned.txt");
        File.WriteAllLines(file, ids.EnumerateArray().Select(item => item.GetString()!));
        args.AddRange(["--keep-scanned", file]);
        return args;
    }

    // Each named property, when given, is an absolute path for a new folder in an existing one.
    private static string? ValidateNewFolders(JsonElement input, IEnumerable<string> properties)
    {
        foreach (var property in properties)
        {
            if (!input.TryGetProperty(property, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 1024 } path || !Path.IsPathFullyQualified(path)) return $"{property} must be an absolute path";
            if (File.Exists(path) || Directory.Exists(path)) return $"{property} already exists";
            if (!Directory.Exists(Path.GetDirectoryName(path))) return $"{property} parent folder does not exist";
        }
        return null;
    }

    private static bool Flag(JsonElement input, string property) => input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    private static string[] Strings(JsonElement input, string property) => [.. input.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];

    private static Operation ReportOperation(string name, string marker, string description) =>
        new(marker, OutputKind.Report, new RuntimeTool(name, description, Schema(new JsonObject { ["path"] = PdfPath() }, "path")),
            (input, _) => [marker[..marker.IndexOf(' ', StringComparison.Ordinal)], String(input, "path"), "--json"], 120) { SuccessCodes = [0, 3] };

    private static JsonObject Text(int maxLength, string? pattern = null, string? description = null)
    {
        var value = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = maxLength };
        if (pattern is not null) value["pattern"] = pattern;
        if (description is not null) value["description"] = description;
        return value;
    }

    // Text passed to KillerPDF on its own, which must not look like an option.
    private static JsonObject Positional(int maxLength, int minLength = 1) => new() { ["type"] = "string", ["minLength"] = minLength, ["maxLength"] = maxLength, ["pattern"] = "^(?!--)" };
    private static JsonObject FilePath(string description, string? pattern = null) { var value = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = description }; if (pattern is not null) value["pattern"] = pattern; return value; }
    private static JsonObject NewFile(string description) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = description };
    private static JsonObject ListOf(JsonObject items, int minItems, int maxItems) => new() { ["type"] = "array", ["items"] = items, ["minItems"] = minItems, ["maxItems"] = maxItems };
    private static JsonObject Range(double minimum, double maximum) => new() { ["type"] = "number", ["minimum"] = minimum, ["maximum"] = maximum };
    private static JsonObject Points(double maximum) => Range(0, maximum);
    private static JsonObject Sheet() => Text(32, "^(?:[A-Za-z0-9]+|\\d+(?:\\.\\d+)?x\\d+(?:\\.\\d+)?)$", "Sheet name such as letter, tabloid or a4, or WxH in points");
    private static JsonObject Choice(params string[] values) => Enum(values);
    private static JsonObject Relationship() => Choice("data", "source", "alternative", "supplement", "unspecified");
    private static JsonObject Language() => new() { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 32, ["pattern"] = "^[A-Za-z0-9_]+$", ["default"] = "eng" };

    // Checks every supplied value against its property schema: type, enum, length, pattern,
    // range and array items.
    private static string? ValidateValues(JsonElement input, JsonObject schema)
    {
        foreach (var (name, node) in schema["properties"]!.AsObject())
            if (node is JsonObject property && input.TryGetProperty(name, out var value) && !Matches(value, property))
                return $"Invalid {name}";
        return null;
    }

    private static bool Matches(JsonElement value, JsonObject schema)
    {
        if (schema["enum"] is JsonArray choices && !choices.Any(choice => choice is not null && JsonNode.DeepEquals(choice, JsonNode.Parse(value.GetRawText())))) return false;
        switch (schema["type"]?.GetValue<string>())
        {
            case "string":
                if (value.ValueKind != JsonValueKind.String) return false;
                var text = value.GetString()!;
                if (Limit(schema, "minLength") is double minLength && text.Length < minLength) return false;
                if (Limit(schema, "maxLength") is double maxLength && text.Length > maxLength) return false;
                return schema["pattern"] is not JsonValue pattern || Regex.IsMatch(text, pattern.GetValue<string>(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            case "integer":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole) && InRange(whole, schema);
            case "number":
                return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && InRange(number, schema);
            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case "array":
                if (value.ValueKind != JsonValueKind.Array) return false;
                var count = value.GetArrayLength();
                if (Limit(schema, "minItems") is double minItems && count < minItems) return false;
                if (Limit(schema, "maxItems") is double maxItems && count > maxItems) return false;
                return schema["items"] is not JsonObject items || value.EnumerateArray().All(item => Matches(item, items));
            default:
                return true;
        }
    }

    private static bool InRange(double number, JsonObject schema) =>
        (Limit(schema, "minimum") is not double minimum || number >= minimum)
        && (Limit(schema, "maximum") is not double maximum || number <= maximum)
        && (Limit(schema, "exclusiveMinimum") is not double exclusive || number > exclusive);

    private static double? Limit(JsonObject schema, string name) =>
        schema[name] is JsonNode node ? double.Parse(node.ToJsonString(), CultureInfo.InvariantCulture) : null;

    // Each named property holds one absolute path, or a list of them, to an existing local file.
    private static string? ValidateFiles(JsonElement input, IEnumerable<string> properties)
    {
        foreach (var property in properties)
        {
            if (!input.TryGetProperty(property, out var value)) continue;
            var paths = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];
            foreach (var path in paths)
            {
                if (path.ValueKind != JsonValueKind.String || path.GetString() is not { Length: > 0 and <= 1024 } text || !Path.IsPathFullyQualified(text)) return $"{property} must be absolute file paths";
                try
                {
                    var info = new FileInfo(text);
                    if (!info.Exists || info.Length > MaxPdfBytes) return $"Each {property} file must exist and be no larger than 64 MiB";
                }
                catch { return $"Unable to read a {property} file"; }
            }
        }
        return null;
    }
}
