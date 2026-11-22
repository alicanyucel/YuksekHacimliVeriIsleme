using GenericRepository;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scrutor;
using System.Reflection;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.Domain.Entities;
using YuksekHacimliVeriIsleme.Infrastructure.Context;
using YuksekHacimliVeriIsleme.Infrastructure.Options;
using YuksekHacimliVeriIsleme.Infrastructure.Repositories;
using YuksekHacimliVeriIsleme.Infrastructure.Services;

namespace YuksekHacimliVeriIsleme.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("SqlServer"));
            });

            services.AddScoped<IUnitOfWork>(srv => srv.GetRequiredService<ApplicationDbContext>());
            services.Configure<CustomerImportOptions>(configuration.GetSection(CustomerImportOptions.SectionName));
            services.AddScoped<ICustomerImportRepository, CustomerImportRepository>();
            services.AddSingleton<ICustomerImportQueue, CustomerImportQueue>();
            services.AddScoped<ICustomerImportService, CustomerImportService>();
            services.AddScoped<ICustomerImportProcessor, CustomerImportProcessor>();
            services.AddHostedService<CustomerImportBackgroundService>();

            services
                .AddIdentity<AppUser, IdentityRole<Guid>>(cfr =>
                {
                    cfr.Password.RequiredLength = 1;
                    cfr.Password.RequireNonAlphanumeric = false;
                    cfr.Password.RequireUppercase = false;
                    cfr.Password.RequireLowercase = false;
                    cfr.Password.RequireDigit = false;
                    cfr.SignIn.RequireConfirmedEmail = true;
                    cfr.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                    cfr.Lockout.MaxFailedAccessAttempts = 3;
                    cfr.Lockout.AllowedForNewUsers = true;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
            services.ConfigureOptions<JwtTokenOptionsSetup>();
            services.AddAuthentication()
                .AddJwtBearer();
            services.AddAuthorizationBuilder();

            services.Scan(action =>
            {
                action
                .FromAssemblies(Assembly.GetExecutingAssembly())
                .AddClasses(publicOnly: false)
                .UsingRegistrationStrategy(RegistrationStrategy.Skip)
                .AsMatchingInterface()
                .AsImplementedInterfaces()
                .WithScopedLifetime();
            });

            return services;
        }
    }
}
