using System.ComponentModel.DataAnnotations;

namespace Snet.Api.Attribute
{
    /// <summary>
    /// 允许文件类型特性
    /// </summary>
    public class AllowedFileTypeAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;
        public AllowedFileTypeAttribute(string[] extensions)
        {
            _extensions = extensions;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var file = value as IFormFile;
            if (file != null)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_extensions.Contains(extension))
                {
                    return new ValidationResult($"文件类型不被支持，仅允许: {string.Join(", ", _extensions)}");
                }
            }
            return ValidationResult.Success;
        }
    }
}
