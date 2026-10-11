# PB01 — Product Backlog و برنامه Sprint نخست
وضعیت: DRAFT — برنامه‌ریزی، نه مجوز Code یا تأیید Sprint
تاریخ: ۲۰۲۶-۱۰-۱۱
مراجع: B01–B05A، T01–T06

## اصول اولویت‌بندی
- محصول افتتاح کشوری، دامنه کامل مصوب برای ۸۰ کانون است؛ Sprintهای مرحله‌ای به معنی کاهش محصول به MVP نیستند.
- ترتیب فنی باید وابستگی‌ها را رعایت کند؛ هیچ سیاست مالی، داده خردسال، اختیار تفویض یا امتیاز ارزیابی اختراع نشود.
- هر آیتم قبل از Code نیازمند قرارداد، تست پذیرش و بررسی تصمیم‌های باز مرتبط است.
- فرایند: Business → Technical → Product Backlog → Sprint → Code → Code Review → Stage → QA/Testing → Release Approval → Production → Monitoring → Improvement.

## Epicها
| Epic | دامنه | پیش‌نیاز |
|---|---|---|
| EP-01 | Engineering Foundation و CI | T01–T06 |
| EP-02 | Identity, Session, Tenant Isolation | ماتریس مجوز، threat model و T03/T04 |
| EP-03 | National Programs و Local Execution | نسخه/ابلاغ و قرارداد انطباق |
| EP-04 | Learning, Events, Campaigns | شرایط ثبت‌نام/انتشار |
| EP-05 | Content, Site & Form Builder | سیاست انتشار/داده فرم |
| EP-06 | Audience & Communication | رضایت، نمایندگی و حریم خصوصی |
| EP-07 | Resources, Finance & Crowdfunding | مجوز و قرارداد واقعی مالی |
| EP-08 | Collaboration & Sharing | مالکیت و قرارداد اشتراک |
| EP-09 | Evaluation & Reporting | شاخص‌های مصوب و بازبینی انسانی |
| EP-10 | Governed Internal AI | مدل/داده مجاز/ارزیابی و تأیید انسانی |
| EP-11 | Nationwide Release Readiness | آزمون یکپارچگی و عملیات ۸۰ کانون |

## Sprint 01 — Engineering Foundation
**وضعیت:** PLANNED / NOT CODE-AUTHORIZED

### BL-001 — Backend Solution Skeleton
- ایجاد ASP.NET Core solution، Host/API و BuildingBlocks پایه با مرز ماژولی بدون منطق حساس.
- Health endpoint غیرحساس و پاسخ خطای استاندارد.
- معیار پذیرش: build و unit test در محیط CI سبز؛ endpoint سلامت بدون وابستگی به داده واقعی.

### BL-002 — Frontend Workspace
- React + TypeScript با سه app: headquarters، center، audience.
- RTL فارسی، shell مشترک، lint/typecheck و تست smoke.
- معیار پذیرش: هر سه app مستقل build شوند؛ همکار/مربی app چهارم نداشته باشد.

### BL-003 — AI Skeleton
- پروژه Python/PyTorch-ready به‌صورت ساختار و قرارداد آزمایشی؛ بدون دانلود مدل، endpoint عمومی یا اتصال داده واقعی.
- معیار پذیرش: تست‌های پایه و مرز عدم تغییر state رسمی؛ هیچ مدل Production ادعا نشود.

### BL-004 — CI Quality Gates
- GitHub Actions برای build/test/lint/typecheck، بررسی secret و گزارش SHA.
- معیار پذیرش: نتیجه CI روی همان commit قابل بررسی باشد و failure موجب توقف ادامه feature work شود.

### BL-005 — Engineering Documentation
- README اجرای محلی، config نمونه بدون secret، conventions و مرزهای ماژول.
- معیار پذیرش: توسعه‌دهنده/عامل توسعه بتواند با دستورهای مستند پروژه پایه را build/test کند.

### BL-006 — Architecture Guardrails
- آزمون وابستگی ممنوع بین ماژول‌ها و عدم دسترسی AI به persistence عملیاتی.
- معیار پذیرش: حداقل یک نمونه نقض معماری در تست قابل کشف باشد؛ پیاده‌سازی ruleها به طراحی واقعی وابسته است.

## خارج از Sprint 01
- هویت و نشست Production، ثبت‌نام مخاطب، اطلاعات واقعی کودک/خانواده، grants نهایی، پرداخت/تسویه، قواعد امتیازدهی، آموزش مدل، استقرار Stage/Production.
- ایجاد ۸۰ tenant واقعی یا داده عملیاتی واقعی.
- ادعای تکمیل Business یا release readiness.

## Definition of Ready
1. نسخه‌های SDK/Runtime و package manager انتخاب و pin شوند.
2. ساختار solution و قرارداد module boundary بازبینی شود.
3. معیار پذیرش هر BL مشخص و بدون سیاست اختراعی باشد.
4. مجوز Code برای Sprint به‌صورت صریح ثبت شود.

## Definition of Done برای Code Sprint 01
- همه BLهای درون دامنه پیاده‌سازی و مستند باشند.
- build/test/lint/typecheck/architecture tests روی HEAD موفق شوند.
- PR در وضعیت Draft/Open بماند؛ Code Review مستقل ثبت شود.
- Stage/QA/Release/Production صرفاً با دروازه و دستور جداگانه انجام شود.

## گام‌های بعدی
- تعیین و pin نسخه‌های سازگار .NET، Node و Python بر اساس مستندات رسمی و آزمون.
- تهیه Sprint 01 technical implementation plan و سپس درخواست Code Authorization.
- موازی با Foundation، تکمیل قراردادهای Business باز برای Sprintهای دامنه‌ای.
