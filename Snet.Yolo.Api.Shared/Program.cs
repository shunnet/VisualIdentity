
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Snet.Yolo.Api.Handler;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace Snet.Yolo.Api
{
    /// <summary>API 应用入口。</summary>
    public class Program
    {
        /// <summary>启动 API 应用。</summary>
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
            IConfiguration configuration = builder.Configuration.GetSection("ConfigModel");
            ConfigModel config = configuration.Get<ConfigModel>() ?? new ConfigModel();
            HistoryFileHandler handler = HistoryFileHandler.Instance(config.BasePath);
            handler.SetConfig(config);
            using var cleanupTokenSource = new CancellationTokenSource();
            Task cleanupTask = handler.DeleteLogicAsync(cleanupTokenSource.Token);

            builder.Services.Configure<ConfigModel>(configuration);

            builder.Services.AddSingleton(new PoseEstimationCustomKeyPointColorHandler());

            builder.Services.AddSingleton(ManageOperate.Instance(PublicHandler.DefaultSN));
            var maximumUploadBytes = Math.Max(config.MaxModelBytes, config.MaxImageBytes);
            if (maximumUploadBytes <= 0) { throw new InvalidOperationException("ConfigModel upload size limits must be positive."); }
            var maximumRequestBytes = checked(maximumUploadBytes + 1024 * 1024);
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Limits.MaxRequestBodySize = maximumRequestBytes;
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = maximumRequestBytes;
            });
            builder.Services.Configure<JsonOptions>(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.IgnoreReadOnlyProperties = true;
            });
            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(opt =>
            {
                opt.SwaggerDoc("v1", new OpenApiInfo { Title = "Snet", Version = "v1" });
                opt.DescribeAllParametersInCamelCase();
                opt.IgnoreObsoleteActions();
                opt.IgnoreObsoleteProperties();
                var assemblyDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? AppContext.BaseDirectory;
                foreach (var file in Directory.GetFiles(assemblyDirectory))
                {
                    if (Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.IncludeXmlComments(file, true);
                    }
                }
            });

            // Rate limiting — fixed window, configurable via appsettings
            var rateLimitConfig = builder.Configuration.GetSection("RateLimit");
            var permitLimit = rateLimitConfig.GetValue<int>("PermitLimit", 120);
            var windowMinutes = rateLimitConfig.GetValue<int>("WindowMinutes", 1);
            var queueLimit = rateLimitConfig.GetValue<int>("QueueLimit", 20);
            if (permitLimit <= 0 || windowMinutes <= 0 || queueLimit < 0)
            {
                throw new InvalidOperationException("RateLimit values are invalid: PermitLimit and WindowMinutes must be positive, and QueueLimit cannot be negative.");
            }
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("fixed", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(windowMinutes),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit,
                    }));
            });

            // CORS — configured from appsettings, defaults to restrictive
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("RestrictedOrigins", policy =>
                {
                    if (allowedOrigins.Length > 0)
                    {
                        policy.WithOrigins(allowedOrigins)
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                });
            });

            var app = builder.Build();

            // Security headers
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                if (!headers.ContainsKey("Strict-Transport-Security"))
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }
                await next();
            });

            // Swagger only in development
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseRateLimiter();
            app.UseCors("RestrictedOrigins");
            app.MapControllers().RequireRateLimiting("fixed");

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

            try
            {
                await app.RunAsync();
            }
            finally
            {
                await cleanupTokenSource.CancelAsync();
                await cleanupTask;
            }
        }
    }
}
