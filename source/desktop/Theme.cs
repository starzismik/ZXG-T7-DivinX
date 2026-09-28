using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
namespace ZXGT7Guard;
public partial class MainWindow {
 private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 private void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
 private void CloseApp_Click(object sender,RoutedEventArgs e)=>Close();
 private void Info_Click(object sender,RoutedEventArgs e){Page(InfoPage);}
 private void MenuKey_Changed(object sender,SelectionChangedEventArgs e){if(IsLoaded)ApplyKey_Click(sender,new RoutedEventArgs());}
}
