using YuksekHacimliVeriIsleme.Application.Features.Auth.Login;
using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Application.Services
{
    public interface IJwtProvider
    {
        Task<LoginCommandResponse> CreateToken(AppUser user);
    }
}
