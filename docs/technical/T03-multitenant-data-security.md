# T03 — معماری داده و جداسازی چندکانونی
وضعیت: DRAFT — پیشنهاد فنی؛ نیازمند بازبینی و تصمیم رسمی
تاریخ: ۲۰۲۶-۱۰-۱۱
مراجع: B01–B05A، T01، T02

## ۱. تصمیم پیشنهادی
یک PostgreSQL عملیاتی مشترک با جداسازی منطقی tenant، مالکیت صریح رکوردها و دفاع چندلایه پیشنهاد می‌شود. این سند انتخاب قطعی shared-schema یا قرارداد نهایی PostgreSQL RLS نیست؛ هر دو باید با threat model، آزمون و بررسی هزینه عملیاتی تثبیت شوند.

## ۲. دسته‌های دامنه داده
- NATIONAL: برنامه ملی، نسخه و ابلاغ ستادی؛ مالکیت و حق تغییر مرکزی.
- CENTER: کلاس، رویداد، برنامه بومی، محتوا، منابع و شواهد با مالکیت کانون مشخص.
- AUDIENCE: هویت و اطلاعات مخاطب؛ دسترسی از روابط و مجوزهای معتبر، نه فرض مالکیت انحصاری یک کانون.
- SHARED/COLLABORATION: منابع/محتوای مجاز به اشتراک، با مالک و محدوده اشتراک صریح.
- AUDIT: تاریخچه امنیتی و عملیاتی با محدودیت دسترسی مستقل.
- AI-GOVERNANCE: نسخه Dataset، Training، Evaluation، مدل و تأیید انسانی؛ داده عملیاتی به‌صورت خودکار وارد آن نمی‌شود.

## ۳. قواعد غیرقابل دور زدن
1. همه عملیات با actor، action، resource و دامنه مجاز بررسی شوند.
2. صرف center_id ارسالی از مرورگر، مجوز ایجاد نمی‌کند؛ دامنه مؤثر از هویت/عضویت معتبر و مجوز سروری تعیین می‌شود.
3. هر رکورد متعلق به کانون باید مالکیت کانونی معتبر داشته باشد؛ رکوردهای ملی/مشترک نوع دامنه جداگانه دارند.
4. cross-center read/write به‌صورت پیش‌فرض ممنوع است مگر قرارداد اشتراک یا اختیار ستادی معتبر وجود داشته باشد.
5. عضویت چندکانونی نباید به دسترسی ضمنی به همه کانون‌ها تبدیل شود؛ هر عملیات باید در scope صریح اجرا شود.
6. دسترسی ستاد به گزارش‌های تجمیعی به‌معنای دسترسی نامحدود به داده فردی نیست.
7. لغو مجوز در درخواست‌های بعدی مؤثر باشد؛ cache یا token قدیمی نباید دسترسی لغوشده را احیا کند.
8. هیچ query، export، background job، گزارش یا AI data preparation نباید کنترل دامنه را دور بزند.
9. ثبت رخداد حساس باید actor، زمان، scope، operation، نتیجه و مرجع داده را در حد مجاز قابل حسابرسی کند.
10. عدم وجود داده، مقدار صفر یا مجوز مثبت محسوب نمی‌شود.

## ۴. مدل مفهومی پیشنهادی، نه schema قطعی
- Center(id, identity, configuration)
- Principal(id, identity_provider_subject)
- CenterMembership(principal_id, center_id, lifecycle)
- PermissionGrant(principal_id, center_id, permission, grantor, validity/version)
- NationalProgram(id, version, fixed_goals, mandatory_requirements)
- ProgramIssuance(program_version_id, center_id, issuance_lineage)
- LocalExecutionPlan(center_id, program_issuance_id, local_plan_data)
- Activity(center_id, source_module, source_id, plan_id)
- AudienceProfile(principal_or_subject_ref, privacy_context)
- Participation(audience_ref, center_id, activity_ref)
- Evidence(owner_scope, source_ref, access_policy_ref)
- AuditEvent(actor_ref, scope, operation, outcome, correlation)

فهرست فوق فقط موجودیت‌های مفهومی است؛ ستون‌ها، کلیدها، روابط، uniqueness، foreign key و سیاست نگهداری باید در طراحی مدل داده هر bounded context نهایی شوند. هیچ وضعیت چرخه عمر یا سیاست حقوقی از نام فیلدها استنباط نمی‌شود.

## ۵. کنترل‌های PostgreSQL و Backend برای ارزیابی
- ASP.NET Core: middleware احراز هویت، authorization مبتنی بر منبع، explicit scope، query filtering و آزمون‌های ضد IDOR.
- PostgreSQL: ارزیابی Row Level Security و role separation برای دفاع دوم؛ بررسی خطر connection pooling، session variables، migration roles و bypass owner.
- Constraints: تضمین مالکیت/مرجع کانون و integrity در سطح دیتابیس در صورت امکان؛ کلیدهای مرکب یا روش‌های معادل برای جلوگیری از پیوند میان tenantهای ناسازگار.
- Background jobs/exports: context امنیتی صریح، scope محدود، audit و عدم استفاده از privileged bypass بدون قرارداد.
- داده ملی و مشترک: سیاست دسترسی مستقل از center-owned records، بدون شرط عمومی «همه داده‌ها center_id دارند».

## ۶. سناریوهای امنیتی اجباری
- SEC-01: کاربر A با تغییر URL/body به center B نمی‌تواند داده خصوصی B را بخواند یا تغییر دهد.
- SEC-02: کاربر دارای عضویت A و B فقط در scope صریح و مجاز عملیات انجام می‌دهد.
- SEC-03: لغو مجوز همکار A دسترسی درخواست بعدی او را می‌بندد حتی اگر نشست قبلی معتبر باشد.
- SEC-04: گزارش کشوری داده فردی فراتر از مجوز ستاد افشا نمی‌کند.
- SEC-05: query گزارش، export و background job همان کنترل‌های دامنه را اعمال می‌کنند.
- SEC-06: مخاطب دارای ثبت‌نام در A و B تنها داده‌های خود و اطلاعات عمومی مجاز را می‌بیند.
- SEC-07: نسخه ملی توسط مدیر کانون قابل ویرایش نیست؛ طرح محلی کانون دیگر نیز قابل تغییر نیست.
- SEC-08: عملیات اشتراک محتوا، مالکیت منبع و مرز مشاهده را تغییر ضمنی نمی‌دهد.
- SEC-09: خطای context، مجوز یا RLS به رد امن می‌انجامد، نه اجرای بدون فیلتر.
- SEC-10: داده ارسالی به AI تنها subset مجاز و لازم است؛ AI دسترسی مستقیم نامحدود به PostgreSQL ندارد.

## ۷. تصمیم‌های باز برای T04
1. مدل فیزیکی multi-tenancy و استفاده قطعی از RLS، با PoC و آزمون ضد bypass.
2. مدل identity/session و رابطه Principal با مخاطب/همکار چندکانونی.
3. ماتریس نقش × عملیات × دامنه × شرط و حق تفویض.
4. قرارداد اشتراک داده، نمایندگی قانونی خردسال و دسترسی ستاد.
5. migration strategy، backup/restore، encryption، retention و حذف داده.
6. SLA/ظرفیت واقعی و طراحی indexes/partitioning پس از بارسنجی.

## ۸. دروازه ورود به Code
پیش از پیاده‌سازی داده حساس: threat model، قرارداد مجوز، طراحی schema و tenant isolation، تست‌های منفی و تصمیم‌های حقوقی مرتبط باید تصویب شوند. این سند هنوز اجازه ساخت schema نهایی یا دسترسی به داده واقعی مخاطبان نیست.
