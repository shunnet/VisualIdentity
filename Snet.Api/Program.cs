using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Snet.Identify.models.@enum;

namespace Snet.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(opt =>
            {
                opt.SwaggerDoc("v1", new OpenApiInfo { Title = "YSAI", Version = "v1" });
                opt.MapType<IdentifyType>(() => new OpenApiSchema
                {
                    Type = "string",
                    Enum = Enum.GetNames(typeof(IdentifyType))
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
