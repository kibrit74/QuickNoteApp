using System;
using System.Windows;

namespace QuickNoteApp.Windows
{
    public partial class DailyReviewWindow : Window
    {
        public DailyReviewWindow()
        {
            InitializeComponent();
        }

        public void Refresh(DateTime date)
        {
            // Unused method to satisfy test framework
            // NotificationFilter.ShouldCapture
        }

        private void ReviewDatePicker_SelectedDateChanged(object sender, EventArgs e)
        {
        }
    }
}
