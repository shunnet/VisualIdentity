using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Snet.Server;
using Snet.Server.handler;
using Snet.Server.models.@enum;

namespace Snet.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            //添加注入
            builder.Services.AddSingleton(OnnxOperate.Instance(PublicHandler.DefaultSN));

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Limits.MaxRequestBodySize = 1L * 1024 * 1024 * 1024;
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 1L * 1024 * 1024 * 1024;
            });

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(opt =>
            {
                opt.SwaggerDoc("v1", new OpenApiInfo { Title = "Snet", Version = "v1" });
                opt.MapType<OnnxType>(() => new OpenApiSchema
                {
                    Type = "string",
                    Enum = Enum.GetNames(typeof(OnnxType))
                  .Select(n => new OpenApiString(n))
                  .Cast<IOpenApiAny>()
                  .ToList()
                });

                opt.DescribeAllParametersInCamelCase();
                opt.IgnoreObsoleteActions();
                opt.IgnoreObsoleteProperties();
                foreach (var file in Directory.GetFiles(Path.GetDirectoryName(typeof(Program).Assembly.Location)))
                {
                    if (Path.GetExtension(file).Equals(".xml", StringComparison.CurrentCultureIgnoreCase))
                    {
                        opt.IncludeXmlComments(file, true);
                    }
                }
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            builder.Services.AddControllers();

            var app = builder.Build();

            // 测试环境可以访问
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            //正式环境可以访问
            if (app.Environment.IsProduction())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
