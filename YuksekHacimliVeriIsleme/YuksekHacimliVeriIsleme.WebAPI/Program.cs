using DefaultCorsPolicyNugetPackage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using YuksekHacimliVeriIsleme.Application;
using YuksekHacimliVeriIsleme.Infrastructure;
using YuksekHacimliVeriIsleme.WebAPI.Middlewares;

var builder = WebApplication.CreateBuilder(args);
var maxUploadSize = builder.Configuration.GetValue<long>("CustomerImport:MaxFileSizeBytes", 1_073_741_824);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadSize + 1_048_576);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadSize + 1_048_576);

builder.Services.AddDefaultCors();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(setup =>
{
    var jwtSecuritySheme = new OpenApiSecurityScheme
    {
        BearerFormat = "JWT",
        Name = "JWT Authentication",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Description = "Put **_ONLY_** yourt JWT Bearer token on textbox below!",

        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    setup.AddSecurityDefinition(jwtSecuritySheme.Reference.Id, jwtSecuritySheme);

    setup.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { jwtSecuritySheme, Array.Empty<string>() }
                });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.UseExceptionHandler();

app.MapControllers();

ExtensionsMiddleware.CreateFirstUser(app);

app.Run();
