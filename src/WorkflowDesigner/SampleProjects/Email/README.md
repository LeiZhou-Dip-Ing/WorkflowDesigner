# Email Action demo

Open `EmailDemo.json` as a local Project in Designer. The `SendDemoEmail` method contains the built-in **Mail** Action and addresses one message to `dip_ing_leizhou@outlook.com`.

For the requested Outlook.com demo, open **Settings** below **Create**, choose **Microsoft Graph**, enter `LeiJun@outlook.com` as **Expected sender address** and the Microsoft Entra **Application (client) ID**, then save. Select **Connect Microsoft account**, open the displayed Microsoft sign-in page, enter the displayed code, and sign in as `LeiJun@outlook.com`. Confirm that this address appears as the connected account before running `SendDemoEmail`. The application registration must support personal Microsoft accounts, allow public-client flows, and include delegated Microsoft Graph `Mail.Send` permission.

For an SMTP sender, choose **SMTP** instead and configure SMTPHost, SMTPPort, SenderAddress, Password, UserName, and UseSSL. QQ Mail requires its SMTP authorization code in Password, not the account password.

No credentials or OAuth tokens are included in this Project. Importing or deploying the Project does not send an email; only running the method does. A successful Microsoft Graph response means Microsoft accepted the send request, not that the recipient has confirmed delivery. Graph attachments are currently limited to files smaller than 3 MB.
