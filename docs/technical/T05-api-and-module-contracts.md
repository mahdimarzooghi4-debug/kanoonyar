# T05 — قرارداد API و ارتباط ماژول‌ها
وضعیت: DRAFT — طراحی قرارداد، نه OpenAPI نهایی یا مجوز Code
تاریخ: ۲۰۲۶-۱۰-۱۱
مراجع: B01–B05A، T01–T04

## ۱. اصول API
- هسته عملیاتی ASP.NET Core مالک APIها و قواعد کسب‌وکار است؛ سه تجربه React از مسیرهای مجاز استفاده می‌کنند.
- APIها نسخه‌دار باشند؛ نمونه پایه پیشنهادی: /api/v1. تغییر ناسازگار نیازمند نسخه/مهاجرت قرارداد است.
- احراز هویت از Keycloak/OIDC و کنترل authorization مبتنی بر actor + operation + resource + scope در Backend انجام شود.
- پاسخ خطا ساختار استاندارد و قابل ردیابی داشته باشد (Problem Details / RFC 9457 گزینه پیشنهادی)، بدون افشای داده خصوصی.
- برای عملیات تکرارپذیر حساس، قرارداد idempotency، concurrency و audit در طراحی endpoint تثبیت شود.
- فیلتر، صفحه‌بندی و مرتب‌سازی با allowlist و سقف مصوب انجام شود؛ عدد و سقف هنوز باز است.

## ۲. نمونه گروه‌های API (نام‌گذاری پیشنهادی؛ نهایی نیست)
| گروه | عملیات نمونه | مالک |
|---|---|---|
| /api/v1/me | هویت و context مجاز کاربر | IdentityAccess |
| /api/v1/centers | شناسنامه و تنظیمات کانون | CenterManagement |
| /api/v1/centers/{centerId}/memberships | اعضا و انتساب‌های مجاز | IdentityAccess |
| /api/v1/national-programs | نسخه‌ها و ابلاغ ملی | NationalPrograms |
| /api/v1/centers/{centerId}/execution-plans | طرح اجرای بومی | LocalPrograms |
| /api/v1/centers/{centerId}/classes | کلاس‌ها، جلسات، ثبت‌نام و حضور | Learning |
| /api/v1/centers/{centerId}/events | رویداد و مسابقات | Events |
| /api/v1/centers/{centerId}/campaigns | پویش‌ها | Campaigns |
| /api/v1/centers/{centerId}/content | صفحه و محتوای کانون | ContentWeb |
| /api/v1/audience/me | اطلاعات و فعالیت‌های مخاطب مجاز | Audience |
| /api/v1/centers/{centerId}/resources | منابع و اسناد مجاز | FinanceResources |
| /api/v1/centers/{centerId}/fundraising | پروژه‌های تأمین مالی؛ بدون فرض پرداخت واقعی | Crowdfunding |
| /api/v1/collaborations | درخواست‌های همکاری مجاز | Collaboration |
| /api/v1/headquarters/reports | نماهای ستادی مجاز | EvaluationReporting |
| /api/v1/ai/assist | درخواست پیشنهاد AI در چارچوب مجوز | AIOrchestration |

مسیرها illustrative هستند؛ verb، schema، state machine و مجوز هر عملیات در قرارداد endpoint جداگانه مشخص خواهد شد.

## ۳. قرارداد امنیت درخواست
- centerId ارسالی کاربر صرفاً درخواست scope است و هرگز مجوز محسوب نمی‌شود.
- برای resource متعلق به کانون، مالکیت واقعی در سرور بارگذاری و با scope و grant تطبیق داده شود.
- نشست OIDC معتبر به‌تنهایی برای عملیات کانونی کافی نیست.
- درخواست‌های عمومی، مخاطب، کانون و ستاد مسیرهای authorization مستقل دارند.
- داده‌های حساس مخاطبان و خردسالان در responseها فقط در حد نیاز و مبنای قانونی معتبر نمایش داده شوند.
- export، background jobs، گزارش‌ها و AI همان کنترل دامنه و مجوز را رعایت کنند.

## ۴. قواعد write و خطا
- هر mutation باید preconditionهای دامنه را بررسی کند و در شکست، تغییر نیمه‌کاره معتبر تلقی نشود.
- پیشنهاد HTTP: 400 ورودی نامعتبر؛ 401 هویت نامعتبر؛ 403 عدم مجوز؛ 404 عدم دسترسی/نبود منبع با سیاست جلوگیری از افشا؛ 409 تعارض/نسخه کهنه؛ 422 ناسازگاری معنایی در صورت قرارداد؛ 429 محدودیت درخواست؛ 5xx خطای زیرساخت. نگاشت قطعی هر خطا بعداً در OpenAPI تصویب شود.
- correlation id برای ردیابی، بدون ذخیره secret/token در لاگ.
- idempotency key و optimistic concurrency برای عملیات حساس مطابق قرارداد دامنه؛ تکرار نباید ثبت‌نام، ابلاغ، grant یا تراکنش مضاعف بسازد.
- تغییرات مهم باید audit معتبر داشته باشند؛ شکست audit لازم نباید با موفقیت ظاهری همراه باشد.

## ۵. مرز ماژول‌ها
- هیچ ماژول نباید مستقیم جدول خصوصی ماژول دیگر را تغییر دهد.
- درون Modular Monolith، قراردادهای application services/read contracts برای فراخوانی هم‌زمان به کار روند.
- برای side effectهای غیرهم‌زمان، domain/integration event با شناسه پایدار، نسخه schema، زمان و مرجع دامنه پیشنهاد می‌شود.
- در صورت نیاز به تحویل قابل اتکا، transactional outbox و مصرف idempotent بررسی شود؛ NATS/Temporal هنوز تصمیم قطعی نیستند.
- داده مرجع یک بار در ماژول مالک ثبت شود؛ گزارش‌ها از read model مجاز و lineage استفاده کنند.

## ۶. نمونه جریان سراسری
1. ستاد نسخه برنامه ملی را در NationalPrograms ثبت و به کانون‌های مشمول ابلاغ می‌کند.
2. LocalPrograms طرح بومی هر کانون را به نسخه ابلاغی پیوند می‌دهد.
3. Learning/Events/Campaigns فعالیت‌های واقعی را به طرح وصل می‌کنند.
4. Audience ثبت‌نام/مشارکت مجاز را ثبت می‌کند.
5. EvaluationReporting از داده‌های معتبر و مجاز، گزارش دارای ارجاع منبع تولید می‌کند.
6. هیچ گزارش یا AI، اجرای واقعی، نتیجه کیفی یا مقدار ناموجود را اختراع نمی‌کند.

## ۷. مرز AI
- AIOrchestration در ASP.NET Core درخواست را از نظر actor، scope، داده لازم و نوع عملیات بررسی می‌کند.
- Python/PyTorch داده حداقلی مجاز را برای تحلیل/پیشنهاد دریافت می‌کند و state رسمی را مستقیماً تغییر نمی‌دهد.
- پاسخ AI شامل provenance/model version و وضعیت «پیشنهاد» در صورت امکان قرارداد خواهد بود؛ جزئیات schema در تصمیم بعدی تعیین می‌شود.
- مدل، transport، isolation، timeout، queue، dataset و promotion هنوز انتخاب نشده‌اند.
- فراخوانی AI حق تفویض مجوز یا bypass کنترل‌های Backend ندارد.

## ۸. بسته قراردادهای بعدی قبل از Code
- OpenAPI دقیق برای IdentityAccess/CenterManagement و NationalPrograms/LocalPrograms.
- DTOها، schema validation، policy checks، idempotency و concurrency.
- قرارداد رویدادهای بین‌ماژولی و read modelهای ارزیاب‌یار.
- threat model برای API و مدل داده چندکانونی.
- آزمون‌های contract، negative authorization، IDOR، cross-tenant، concurrent mutation و AI isolation.
- تصویب ماتریس مجوزهای B02 و سیاست‌های باز مرتبط با خردسالان/مالی پیش از ساخت endpointهای حساس.

## ۹. وضعیت
T05 نقشه اولیه API است، نه فهرست endpointهای پیاده‌سازی‌شده. Code، CI، Stage و Production هنوز آغاز نشده‌اند.
