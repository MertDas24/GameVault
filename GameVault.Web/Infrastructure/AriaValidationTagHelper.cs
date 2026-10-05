using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// asp-for kullanan form alanlarında doğrulama hatası varsa aria-invalid ve
/// aria-describedby ekler; ekran okuyucular hatayı alanla birlikte okur.
/// Hata metni, "{id}-error" kimlikli öğede gösterilir.
/// </summary>
[HtmlTargetElement("input", Attributes = "asp-for")]
[HtmlTargetElement("select", Attributes = "asp-for")]
[HtmlTargetElement("textarea", Attributes = "asp-for")]
public sealed class AriaValidationTagHelper : TagHelper
{
    public override int Order => 1000; // yerleşik tag helper'lar name/id ürettikten sonra

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!output.Attributes.TryGetAttribute("name", out var nameAttr) || nameAttr.Value?.ToString() is not { } name)
        {
            return;
        }

        if (ViewContext.ViewData.ModelState.TryGetValue(name, out var entry) && entry.Errors.Count > 0)
        {
            var id = output.Attributes.TryGetAttribute("id", out var idAttr)
                ? idAttr.Value?.ToString()
                : TagBuilder.CreateSanitizedId(name, "_");
            output.Attributes.SetAttribute("aria-invalid", "true");
            output.Attributes.SetAttribute("aria-describedby", $"{id}-error");
        }
    }
}
