using System.Security.Claims;
using GameVault.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Infrastructure;

/// <summary>Arayüzde gösterilen adı oturum kimliğine ekler.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string DisplayNameClaim = "gv:display_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        }

        return identity;
    }
}
