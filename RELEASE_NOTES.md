# v0.1.7

- Test remote SOCKS TCP and DNS before applying Windows routes, then test TCP through the TUN. Restore routes if the same TCP target works through SOCKS but fails through Windows routing.
- Use the configured DNS addresses. Report DNS failures instead of silently dropping them.
- For the default Cloudflare DNS only, fall back from blocked TCP/53 to DNS over HTTPS through SSH/SOCKS. Never send fallback DNS over the physical network.
- Show tun2socks logs, effective routes and DNS request/answer/failure counters in the connection log.

The real server and PC still need an on-device test. Connection alone is not proof of working internet.

# v0.1.6 - endpoint route discovery

- Normalize IPv4-mapped IPv6 socket endpoints to IPv4 before route discovery; an IPv4 connection must not require an IPv6 default route.
- Use Windows GetBestRoute2 with the source address of the TLS socket that already connected. This removes reliance on Find-NetRoute/default-route CIM queries and supports valid virtual, PPP and on-link uplinks.
- Keep a longest-prefix/combined-metric route-table fallback if native lookup fails. Never choose the tunnel itself or loopback.
- Show route/interface inventories, query errors, TLS source address, selected interface index and gateway in the connection log, including failures.
- Test native selection when PowerShell route/interface discovery fails, as well as route/DNS restore and existing routing, UI and installer regressions.

His exact PC/network remains to be tested. No claim of a confirmed fix on that device; the earlier build did not cover its route-query failure.

## English

Update to the Fluent TLS Tunnel preview for Windows x64. Still a **pre-release**.

### Downloads
- **Fluent-TLS-Tunnel-installer.exe**: recommended. Includes the .NET runtime; installs per user without an administrator requirement.
- **Fluent-TLS-Tunnel-portable.zip**: unzip and run FluentTlsTunnel.exe. Includes the runtime.
- **Fluent-TLS-Tunnel-runtime-dependent.exe**: smaller installer; requires the .NET 8 Desktop Runtime (x64).

### Changes
- The connected state now matches the Windows 11 Settings style: a compact card with a status check, host and SOCKS5 address, and the connection timer.
- The Connect/Disconnect button uses the standard WinUI 3 accent style and default size instead of the large custom green button.
- The status banner appears only when there is a message or an error.
- Smaller, native-style title area; the Save profile button uses the standard button height.

Everything else is unchanged from v0.1.0, including the notes: this is a SOCKS5 tunnel, not a full-device VPN; custom DNS has not yet been runtime-tested on a real adapter; the binaries are unsigned, so Windows may show a SmartScreen warning.

## العربية

تحديث للنسخة التجريبية من Fluent TLS Tunnel لويندوز بمعمارية x64. ما زال الإصدار **pre-release**.

### التنزيلات
- **Fluent-TLS-Tunnel-installer.exe**: الخيار الموصى به. يتضمن بيئة .NET ويُثبَّت لحساب المستخدم دون اشتراط صلاحية المسؤول.
- **Fluent-TLS-Tunnel-portable.zip**: فك الضغط وشغّل FluentTlsTunnel.exe. يتضمن بيئة التشغيل.
- **Fluent-TLS-Tunnel-runtime-dependent.exe**: مُثبّت أصغر يحتاج .NET 8 Desktop Runtime بمعمارية x64.

### التغييرات
- أصبحت حالة الاتصال بنفس نمط تطبيق الإعدادات في ويندوز 11: بطاقة مدمجة فيها علامة الحالة وعنوان الخادم وSOCKS5 وعداد مدة الاتصال.
- زر الاتصال وقطع الاتصال يستخدم نمط WinUI 3 القياسي وحجمه الافتراضي بدل الزر الأخضر الكبير المخصص.
- يظهر شريط الحالة فقط عند وجود رسالة أو خطأ.
- منطقة الترويسة أصغر وبنمط أصلي، وزر حفظ الإعدادات بالارتفاع القياسي.

بقية المزايا والملاحظات كما في v0.1.0: الأداة نفق SOCKS5 وليست VPN للجهاز بالكامل، ولم يُختبر DNS المخصص بعد على محول فعلي، والملفات غير موقّعة رقميًا وقد يظهر تحذير SmartScreen.

brought to you by [Instinct](https://instinct.com)