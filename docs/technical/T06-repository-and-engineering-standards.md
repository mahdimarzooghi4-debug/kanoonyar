# T06 — ساختار مخزن، Solution و استانداردهای توسعه
وضعیت: DRAFT — طرح اجرایی، نه ساخت پروژه یا آغاز Sprint
تاریخ: ۲۰۲۶-۱۰-۱۱
مراجع: B01–B05A و T01–T05

## ۱. ساختار پیشنهادی Monorepo
```text
kanoonyar/
  docs/
    business/
    technical/
    decisions/
    reviews/
  backend/
    Kanoonyar.sln
    src/
      Kanoonyar.Api/
      Kanoonyar.Host/
      Kanoonyar.BuildingBlocks/
      Modules/
        IdentityAccess/
        CenterManagement/
        NationalPrograms/
        LocalPrograms/
        Learning/
        Events/
        Campaigns/
        ContentWeb/
        Audience/
        FinanceResources/
        Crowdfunding/
        Collaboration/
        EvaluationReporting/
        AIOrchestration/
    tests/
      Unit/
      Integration/
      Architecture/
      ApiContract/
      Security/
  frontend/
    apps/
      headquarters/
      center/
      audience/
    packages/
      ui/
      api-client/
      auth/
      shared-types/
    tests/
      e2e/
  ai/
    src/
    tests/
    governance/
  infrastructure/
    local/
    ci/
    deployment/
  .github/
    workflows/
```
این درخت طرح است و هنوز فایل‌ها یا پروژه‌های اجرایی آن ساخته نشده‌اند.

## ۲. قرارداد Backend
- C# / ASP.NET Core؛ انتخاب نسخه LTS و pin دقیق SDK/پکیج‌ها در اولین بسته Code.
- یک Host عملیاتی و ماژول‌های دارای قرارداد مستقل؛ جداسازی منطقی از ابتدا، نه microserviceهای اجباری.
- dependency direction از API به application contracts و از پیاده‌سازی به abstractions؛ وابستگی دوری و دسترسی مستقیم ماژول به persistence خصوصی دیگری ممنوع.
- domain logic در application/domain modules؛ endpoint و controller فقط هماهنگی ورودی/خروجی و مجوز.
- PostgreSQL به‌عنوان منبع حقیقت؛ schema/migration با مالکیت روشن و آزمون migration.
- health/readiness، structured logging، correlation و خطاهای استاندارد؛ بدون افشای token/secret/PII.

## ۳. قرارداد Frontend
- React + TypeScript؛ سه app برای ستاد، پنل واحد کانون و پرتال مخاطب.
- RTL فارسی، دسترس‌پذیری، طراحی واکنش‌گرا و مؤلفه‌های UI مشترک.
- client API نسخه‌دار و type-safe مبتنی بر OpenAPI پس از تصویب قراردادها.
- auth و active context مشترک؛ حق دسترسی در Backend enforce می‌شود و frontend تنها UX مناسب ارائه می‌کند.
- مسیر مربی/مسئول محتوا/مسئول مالی زیر همان app مرکز و بر اساس grant مؤثر است.

## ۴. قرارداد AI
- Python + PyTorch فقط برای وظایف تخصصی AI.
- قرارداد داده ورودی حداقلی، provenance و نسخه مدل؛ بدون دسترسی مستقیم نامحدود به PostgreSQL یا حق تغییر state رسمی.
- train/evaluation/promotion از runtime عملیاتی جدا و تحت تأیید انسانی.
- مدل، transport، زیرساخت، dependency versions و سیاست داده هنوز بازند.

## ۵. کیفیت و ابزارها
- Backend: unit، integration با PostgreSQL واقعی در CI، migration، API contract، architecture-boundary و security/tenant tests.
- Frontend: typecheck، lint، component tests و Playwright E2E برای سه تجربه.
- AI: schema contract، permission boundary، deterministic fixtures و آزمون fail-closed.
- GitHub Actions: build، test، lint، dependency/security scanning، artifact و گزارش نتیجه بر اساس SHA.
- Code Review: بازبینی قرارداد، امنیت، tenant isolation، migration، race/idempotency، تست و observability.
- CI سبز تنها اثبات موفقیت تست‌های اجراشده است؛ Stage/QA/Release/Production دروازه‌های مستقل دارند.

## ۶. قواعد توسعه و تغییر
- هر بسته: Business contract → Technical contract → Backlog/Sprint → Code → Tests → CI → Code Review.
- بدون تأیید سیاست‌های مالی، نمایندگی خردسالان، امتیازدهی یا تفویض اختیار، منطق آن‌ها پیاده‌سازی نشود.
- هیچ credential، داده واقعی مخاطب یا secret وارد مخزن عمومی نشود.
- migration باید backward-compatibility و rollback/recovery را بررسی کند.
- API و event schemaهای منتشرشده نسخه‌دار و دارای contract tests باشند.
- PRها تا تکمیل Code Review و اجازه صریح برای merge در وضعیت Draft/Open بمانند؛ Stage/Production بدون دستور صریح انجام نشود.

## ۷. بسته نخست پیشنهادی Code — فقط Foundation
1. ایجاد solution و پروژه‌های پایه ASP.NET Core با health endpoint و error handling استاندارد.
2. ایجاد workspaceهای React/TypeScript برای سه محیط با UI placeholder، RTL و design primitives.
3. ایجاد پروژه Python AI به‌صورت skeleton بدون مدل یا دسترسی داده.
4. راه‌اندازی CI برای build/test/typecheck/lint و آزمون معماری پایه.
5. اضافه‌کردن README توسعه محلی، قرارداد config بدون secret و نمونه‌های test-only.

این بسته **هیچ** ثبت‌نام واقعی مخاطب، مجوز مالی، مدل AI عملیاتی، grant نهایی یا داده Production ایجاد نمی‌کند.

## ۸. تصمیم‌های باز پیش از اجرای بسته نخست
- انتخاب و pin نسخه‌های واقعی SDK/Node/Python و کتابخانه‌های پایه؛ باید با compatibility و CI بررسی شوند.
- تأیید ساختار نهایی solution و مرز assembly/module.
- تصمیم درباره package manager و test runner frontend.
- سیاست branch/PR و نام‌گذاری migration.

## ۹. وضعیت
این سند برنامه‌ریزی فنی است. هنوز Backlog/Sprint تصویب یا Code/CI اجرا نشده است.
