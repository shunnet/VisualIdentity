using CommunityToolkit.Mvvm.Input;
using Snet.Core.handler;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Core.mvvm;
using System.ComponentModel;

namespace Snet.Yolo.Tool.Data
{

    public class NamesModel : BindNotify
    {
        [Description("下标")]
        [Browsable(false)]
        public int Index
        {
            get => GetProperty(() => Index);
            set => SetProperty(() => Index, value);
        }

        [Description("名称")]
        public string Name
        {
            get => GetProperty(() => Name);
            set => SetProperty(() => Name, value);
        }

        [Description("描述")]
        public string Describe
        {
            get => GetProperty(() => Describe);
            set => SetProperty(() => Describe, value);
        }



        [Browsable(false)]
        public string Path { get; private set; }
        [Browsable(false)]
        public IAsyncRelayCommand SelectPath => selectPath ??= new AsyncRelayCommand(SelectPathAsync);
        [Browsable(false)]
        private IAsyncRelayCommand selectPath;
        [Browsable(false)]
        private Task SelectPathAsync()
        {
            Path = Win32Handler.Select("请选择文件夹".GetLanguageValue(), true);
            return Task.CompletedTask;
        }


        public bool IsNull()
        {
            if (Name.IsNullOrWhiteSpace())
            {
                return true;
            }
            if (Describe.IsNullOrWhiteSpace())
            {
                return true;
            }
            return false;
        }
    }
}
