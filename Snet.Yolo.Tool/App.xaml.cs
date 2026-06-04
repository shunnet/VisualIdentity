using Microsoft.Extensions.DependencyInjection;
using Snet.Core.handler;
using Snet.Log;
using Snet.Model.data;
using Snet.Windows.Controls.property;
using Snet.Windows.Core.handler;
using System.Windows;

namespace Snet.Yolo.Tool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 语言操作
        /// </summary>
        public readonly static LanguageModel LanguageOperate = new LanguageModel("Snet.Yolo.Tool", "Language", "Snet.Yolo.Tool.dll");

        /// <summary>
        /// 在应用程序关闭时发生
        /// </summary>
        private void OnExit(object sender, ExitEventArgs e)
        {
            InjectionWpf.ClearService();
        }

        /// <summary>
        /// 在加载应用程序时发生
        /// </summary>
        private void OnStartup(object sender, StartupEventArgs e)
        {
            //注入参数设置
            PropertyControl control = new PropertyControl();
            control.ButtonVisibility = Visibility.Visible;
            InjectionWpf.AddService(s =>
            {
                s.AddSingleton(control);
            });

            //启动全局异常捕捉
            RegisterEvents();

            //加载本地自定义图标
            IconsHandler.Loading("pack://application:,,,/Snet.Yolo.Tool;component/Resources/Icons.xaml");

            //打开主窗口
            InjectionWpf.Window<MainWindow, MainWindowViewModel>(true).Show();
        }

        #region 全局异常捕捉

        /// <summary>
        /// 全局异常捕捉
        /// </summary>
        private void RegisterEvents()
        {
            //Task线程内未捕获异常处理事件
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            //UI线程未捕获异常处理事件（UI主线程）
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        //Task线程报错
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var exception = e.Exception as Exception;
                if (exception.HResult == -2146233088)
                    return;

                if (exception != null)
                {
                    HandleException(exception);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                e.SetObserved();
            }
        }

        //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                if (exception != null)
                {
                    HandleException(exception);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                //ignore
            }
        }

        //UI线程未捕获异常处理事件（UI主线程）
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                HandleException(e.Exception);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                //处理完后，我们需要将Handler=true表示已此异常已处理过
                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理异常到界面显示与本地日志记录
        /// </summary>
        private void HandleException(Exception e)
        {
            // Always log first — don't depend on UI being available
            LogHelper.Error($"{e.Source}: {e.Message}\r\n{e.StackTrace}", "Snet.Yolo.Tool.log", e);

            if (Application.Current == null)
                return;

            // Fire-and-forget the UI notification so we don't block the exception handler
            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    string msg = FormatExceptionMessage(e);
                    await Snet.Windows.Controls.message.MessageBox.Show(
                        msg,
                        LanguageOperate.GetLanguageValue("全局异常捕获"),
                        Snet.Windows.Controls.@enum.MessageBoxButton.OK,
                        Snet.Windows.Controls.@enum.MessageBoxImage.Exclamation);
                }
                catch
                {
                    // Don't let MessageBox failure mask the original exception
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static string FormatExceptionMessage(Exception e)
        {
            string source = e.Source ?? string.Empty;
            string message = e.Message ?? string.Empty;
            string stackTrace = e.StackTrace ?? string.Empty;

            if (!string.IsNullOrEmpty(source))
            {
                var msg = source;
                if (!string.IsNullOrEmpty(message))
                    msg += $"\r\n{message}";
                if (!string.IsNullOrEmpty(stackTrace))
                    msg += $"\r\n\r\n{stackTrace}";
                return msg;
            }
            if (!string.IsNullOrEmpty(message))
            {
                var msg = message;
                if (!string.IsNullOrEmpty(stackTrace))
                    msg += $"\r\n\r\n{stackTrace}";
                return msg;
            }
            if (!string.IsNullOrEmpty(stackTrace))
                return stackTrace;
            return "未知异常";
        }

        #endregion
    }

}
