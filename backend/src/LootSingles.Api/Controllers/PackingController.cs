using System.Security.Claims;
using LootSingles.Application.Packing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

/// <summary>
/// The packing desk (017-pick-completion-handoff US2).
/// <para>
/// No role check anywhere in here. There are two roles, a packer signs in as a Picker, and a role
/// gate would obstruct packing rather than protect anything (FR-037).
/// </para>
/// </summary>
[ApiController]
[Route("api/packing")]
[Authorize]
public sealed class PackingController(IPackingRepository packingRepository) : ControllerBase
{
    /// <summary>
    /// Resolves whatever landed in the desk's box — a scanned link, a typed order number, or a
    /// scanned TCGplayer identifier — to one order (FR-023, FR-024).
    /// </summary>
    [HttpGet("orders/{code}")]
    public async Task<IActionResult> Resolve(string code, CancellationToken cancellationToken)
    {
        var view = await packingRepository.ResolveAsync(
            PackingCodeResolver.Resolve(Uri.UnescapeDataString(code)),
            cancellationToken
        );

        return view is null ? NotFound(new { error = "order_not_found" }) : Ok(view);
    }

    /// <summary>What is picked and still on the shelf (FR-030).</summary>
    [HttpGet("awaiting")]
    public async Task<IActionResult> Awaiting(CancellationToken cancellationToken) =>
        Ok(await packingRepository.GetAwaitingPackingAsync(cancellationToken));
}
