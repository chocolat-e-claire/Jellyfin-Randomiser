using System.Security.Claims;
using Jellyfin.Data.Entities;
using Jellyfin.Plugin.Randomizer.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Randomizer.Controllers;

[ApiController]
[Authorize]
[Route("Randomizer")]
public sealed class RandomizerController : ControllerBase
{
    private const string PageResource =
        "Jellyfin.Plugin.Randomizer.Configuration.randomizerPage.html";

    private readonly IUserManager users;
    private readonly RandomizerService service;

    public RandomizerController(IUserManager u, RandomizerService s)
    {
        users = u;
        service = s;
    }

    [AllowAnonymous]
    [HttpGet("Status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            enabled = Plugin.Instance?.IsRuntimeEnabled == true
        });
    }

    [HttpGet("Page")]
    public IActionResult Page()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        using var stream =
            typeof(Plugin).Assembly.GetManifestResourceStream(PageResource);
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpGet("Script.js")]
    public IActionResult Script()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.Randomizer.Web.randomizer.js");

        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "application/javascript; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpGet("Styles.css")]
    public IActionResult Styles()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.Randomizer.Web.randomizer.css");

        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "text/css; charset=utf-8");
    }

    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibraryDto>> Libraries()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        return Current() is { } u
            ? Ok(service.Libraries(u))
            : Unauthorized();
    }

    [HttpGet("Genres")]
    public ActionResult<IReadOnlyList<GenreDto>> Genres(
        Guid? libraryId,
        string itemType)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        if (!itemType.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            && !itemType.Equals("Series", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("itemType must be Movie or Series.");
        }

        return Current() is { } u
            ? Ok(service.Genres(u, libraryId, itemType))
            : Unauthorized();
    }

    [HttpGet("Search")]
    public ActionResult<IReadOnlyList<LibraryDto>> Search(
        Guid? libraryId,
        string itemType,
        string? search = null,
        string? genre = null,
        int? limit = null)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        if (!itemType.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            && !itemType.Equals("Series", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("itemType must be Movie or Series.");
        }

        return Current() is { } u
            ? Ok(service.Search(u, libraryId, itemType, search, genre, limit))
            : Unauthorized();
    }

    [HttpPost("Randomize")]
    public ActionResult<RandomizeResult> Randomize(
        [FromBody] RandomizeRequest request)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        if (request.ItemIds.Count > 500)
        {
            return BadRequest("Too many item IDs.");
        }

        return Current() is { } u
            ? service.Randomize(u, request) is { } r
                ? Ok(r)
                : NotFound("No eligible items were found for the current filters.")
            : Unauthorized();
    }

    private static bool IsEnabled() =>
        Plugin.Instance?.IsRuntimeEnabled == true;

    private User? Current()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = User.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.NameIdentifier
            || c.Type.Equals(
                "Jellyfin-UserId",
                StringComparison.OrdinalIgnoreCase)
            || c.Type.Equals(
                "UserId",
                StringComparison.OrdinalIgnoreCase))?.Value;

        return Guid.TryParse(value, out var id)
            ? users.GetUserById(id)
            : null;
    }
}
