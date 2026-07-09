using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickNoteApp.Services;

public enum EmailOpenMethod
{
    GmailWeb,
    OutlookCom
}

public sealed record EmailOpenPlan(EmailOpenMethod Method, string Target);

public static class EmailService
{
    public static void OpenInGmailWeb(string to, string subject, string body)
    {
        try
        {
            var url = $"https://mail.google.com/mail/?view=cm&fs=1&to={Uri.EscapeDataString(to)}&su={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Gmail web tarayıcısı açılamadı: " + ex.Message);
        }
    }

    public static void OpenInOutlookDesktop(string to, string subject, string body)
    {
        try
        {
            CreateOutlookDraft(to, subject, body);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Outlook masaüstü uygulaması başlatılamadı: " + ex.Message);
        }
    }

    public static EmailOpenPlan CreateOutlookDesktopPlan(string to, string subject, string body)
    {
        return new EmailOpenPlan(EmailOpenMethod.OutlookCom, "Outlook.Application");
    }

    private static void CreateOutlookDraft(string to, string subject, string body)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Bu bilgisayarda Outlook masaüstü uygulaması bulunamadı.");

        object? outlook = null;
        object? mail = null;

        try
        {
            outlook = Activator.CreateInstance(outlookType)
                ?? throw new InvalidOperationException("Outlook başlatılamadı.");

            dynamic outlookApp = outlook;
            mail = outlookApp.CreateItem(0);

            dynamic mailItem = mail;
            mailItem.To = to;
            mailItem.Subject = subject;
            mailItem.Body = body;
            mailItem.Display(false);
        }
        finally
        {
            ReleaseComObject(mail);
            ReleaseComObject(outlook);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value == null)
            return;

        try
        {
            if (Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
        }
    }
}
