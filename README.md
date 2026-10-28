<div align="center">

<p align="center">
  <img src="3-AppIcon.png" alt="Fluent TLS Tunnel" width="192" height="192">
</p>

# Fluent TLS Tunnel

**A private path. One simple connection.**

Windows 11 · WinUI 3 · SSH over TLS · SNI · SOCKS5

English | [العربية](#العربية)

brought to you by [Instinct](https://instinct.com)

</div>

## What it does

Fluent TLS Tunnel puts an SSH connection inside a TLS connection with your chosen SNI hostname. A local SOCKS5 listener lets compatible apps use that connection. No stunnel, PuTTY or Docker installation is needed.

- Native Fluent interface with Mica, light/dark/system themes.
- English and formal Arabic, with mirrored right-to-left layout.
- Server, port, SNI and SSH credentials in one place.
- One button to connect, cancel or disconnect.
- Automatic Windows TCP routing with a signed Wintun virtual adapter, IPv4/IPv6 routes and DNS over SSH.
- Tray menu: Connect/Disconnect, Settings, About and Close. Closing the window disconnects, restores routing and fully exits the app; the tray stays available while it runs.
- Passwords remembered securely in Windows Credential Manager.
- Local SOCKS5 on `127.0.0.1:1080`, with a configurable port.
- Optional custom DNS for a selected Windows network adapter.
- Save profiles locally; import/export `.ftlsc` files (JSON inside) without passwords or trusted host keys.
- TLS certificate validation and SSH SHA-256 host-key checks.
- Optional password-encrypted exports: AES-256-GCM, PBKDF2-SHA256 (600,000 iterations), random salt and nonce.
- Connection status and an in-memory log.

## Connect

1. Enter your SSH-over-TLS server address, port, SNI hostname and SSH username/password.
2. Leave TLS certificate verification enabled when the provider supports it. Some tunneling services use mismatched certificates and require it disabled. This reduces TLS protection: check the SSH fingerprint with your provider.
3. Click **Connect**. On first use, verify the SSH host-key fingerprint before trusting it. A changed key needs a fresh decision.
4. Automatic Windows routing is on by default. Approve the Windows administrator prompt. No browser proxy setup is needed. Turn it off in Settings if you prefer manual SOCKS5.
5. Click **Disconnect** when finished.

**TCP routing, not a UDP VPN.** Automatic mode routes Windows IPv4 and IPv6 internet TCP connections through a temporary virtual adapter. DNS uses TCP through the SSH tunnel. UDP-only applications are not supported; QUIC-capable browsers can fall back to TCP. Local network routes and the TLS server endpoint remain direct. It does not register a native Windows VPN profile or badge. There is no persistent kill switch: when the tunnel disconnects, normal networking is restored. Use only servers and networks you are permitted to use.

Existing routes and physical adapter DNS settings are not overwritten. An elevated watchdog removes only app-owned routes and DNS policy on disconnect or app crash. A restricted recovery journal is kept if cleanup fails; use **Restore Windows routing** in Settings. Existing Windows DNS policies for specific domains (such as Windows DNS resiliency) are preserved and do not block routing; automatic mode stops only if another catch-all DNS policy is active. Automatic mode requires local DNS port 53 to be free.

## Custom DNS

Choose your network adapter, enter DNS IP addresses separated by commas, and enable custom DNS before connecting. Windows requests administrator approval. Existing static/DHCP settings are backed up before the change and restored on disconnect or normal exit. If the app crashes or approval to restore is declined, use **Restore DNS** on the next start. Do not delete a pending DNS backup.

This changes the selected Windows adapter's DNS, not DNS resolution performed by the remote SSH server. For Firefox, enabling "Proxy DNS when using SOCKS v5" sends hostname resolution to the SSH server instead of your local adapter DNS. Choose the behavior that fits your needs.

## Privacy & security

Passwords are stored in Windows Credential Manager for the selected server and username, never in profile JSON, exports, logs or process command lines. Profiles contain server addresses, usernames and DNS settings: treat exports as private information. Importing never connects automatically or imports trust records. No telemetry is included.

## Requirements

Windows 11 x64 is recommended. The framework-dependent build needs .NET 8 Desktop Runtime. Downloadable releases will appear after the first preview is approved. This project is under development, not a stable release.

## Credits

Developed by Mohamed Elnaggar. brought to you by [Instinct](https://instinct.com).

MIT-licensed application. SSH uses [SSH.NET](https://github.com/sshnet/SSH.NET) (MIT); TLS uses .NET's SslStream. Third-party notices are included.

---

## العربية

**مسار خاص. اتصال بسيط.**

يغلّف Fluent TLS Tunnel اتصال SSH داخل TLS، مع اسم SNI تحدده بنفسك. يوفر SOCKS5 محليًا للتطبيقات المتوافقة دون تثبيت برامج إضافية.

### المزايا

- واجهة ويندوز أصلية بتصميم Fluent وخلفية Mica.
- العربية الفصحى والإنجليزية، مع عكس تخطيط الواجهة في العربية.
- زر واحد للاتصال والإلغاء وقطع الاتصال.
- حفظ الإعدادات واستيرادها وتصديرها بامتداد `.ftlsc` ومحتوى JSON دون كلمات مرور أو مفاتيح موثوقة.
- SOCKS5 محلي بمنفذ قابل للتغيير، وسجل لحالة الاتصال.
- توجيه TCP تلقائي لويندوز وIPv4 وIPv6، مع DNS عبر SSH.
- قائمة في علبة النظام للاتصال والإعدادات وحول التطبيق والإغلاق.
- حفظ كلمة المرور بأمان في مدير بيانات اعتماد ويندوز.
- DNS مخصص لمحول شبكة تختاره، مع حفظ الإعدادات السابقة واستعادتها.
- التحقق من شهادة TLS وبصمة مفتاح خادم SSH.

### الاستخدام

أدخل عنوان الخادم والمنفذ وSNI وبيانات حساب SSH، ثم اضغط **اتصال**. تحقق من بصمة مفتاح الخادم لدى مزود الخدمة قبل الوثوق به. وافق على طلب صلاحية المسؤول لتفعيل توجيه ويندوز تلقائيًا دون إعداد المتصفح. اضغط **قطع الاتصال** عند الانتهاء.

يُوجّه اتصال TCP في ويندوز تلقائيًا، لا اتصال UDP. يعمل DNS عبر SSH. يبقى اتصال الشبكة المحلية والخادم مباشرًا. لا يُسجّل مؤشر VPN الرسمي. تُحذف المسارات وقاعدة DNS الخاصة بالتطبيق عند قطع الاتصال أو تعطل التطبيق؛ تعود الشبكة العادية ولا توجد خاصية منع تسرب دائمة. إذا فشلت الاستعادة، استخدم استعادة توجيه ويندوز في الإعدادات.

يتطلب تغيير DNS موافقة مسؤول ويندوز. تُحفظ إعدادات المحول السابقة قبل تغييرها، وتُستعاد عند قطع الاتصال أو الخروج الطبيعي. بعد تعطل التطبيق أو رفض موافقة الاستعادة، استخدم **استعادة DNS** عند فتحه مجددًا. يغيّر ذلك DNS المحلي، لا DNS الذي يستخدمه خادم SSH البعيد.

تُحفظ كلمات المرور في مدير بيانات اعتماد ويندوز، ولا تُصدّر مع الإعدادات. تحتوي ملفات الإعدادات على عناوين الخوادم وأسماء المستخدمين؛ تعامل معها بوصفها معلومات خاصة. المشروع قيد التطوير، ولا يُعد إصدارًا مستقرًا بعد.

تطوير محمد النجار. مقدم إليكم من [Instinct](https://instinct.com).


## Download options

Each release provides exactly three Windows x64 downloads:

- **Portable ZIP:** extract all files and run the app.
- **Installer:** installs the app with .NET, Windows App SDK and VC runtime files included.
- **Full offline ZIP:** the same installer, named `Install.exe`, plus Microsoft's original offline installers for .NET Desktop Runtime, Windows App Runtime and Visual C++ x64.

No separate runtime download is needed to run the app. The system-runtime installers in the full offline kit are optional. Keep every extracted portable file together.
