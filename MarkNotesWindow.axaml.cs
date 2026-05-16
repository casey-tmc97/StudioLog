using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class MarkNotesWindow : Window
    {
        public string Notes { get; private set; } = string.Empty;
        public bool WasOkClicked { get; private set; } = false;

        public MarkNotesWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            SubmitAndClose();
        }
        
        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                SubmitAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
        
        private void SubmitAndClose()
        {
            var textBox = this.FindControl<TextBox>("NotesTextBox");
            if (textBox != null)
            {
                Notes = textBox.Text ?? string.Empty;
            }
            
            WasOkClicked = true;
            Close();
        }
    }
}
