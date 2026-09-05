using System.ComponentModel.DataAnnotations;

namespace Snet.Yolo.Api.Attribute
{
    /// <summary>
    /// 允许文件类型特性
    /// </summary>
    public class AllowedFileTypeAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;
        /// <summary>创建允许文件扩展名验证器。</summary>
        /// <param name="extensions">允许的扩展名集合。</param>
        public AllowedFileTypeAttribute(string[] extensions)
        {
            _extensions = extensions;
        }

        /// <inheritdoc />
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
            {
                return new ValidationResult("A file is required.");
            }

            if (file.Length > 0)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_extensions.Contains(extension))
                {
                    return new ValidationResult($"Unsupported file type. Only allowed: {string.Join(", ", _extensions)}");
                }
            }
            else
            {
                return new ValidationResult("The file cannot be empty.");
            }
            return ValidationResult.Success;
        }
    }
}
