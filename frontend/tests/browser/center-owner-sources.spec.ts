import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center','Center-only source catalog');
const uuid = (number: number) => '10000000-0000-4000-8000-' + number.toString().padStart(12,'0');

async function open(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships',route=>route.fulfill({
    status:200,contentType:'application/json',
    body:'{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"},{"centerId":"c2","displayName":"کانون دو","roleCode":"member"}]}'
  }));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('test-token');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}

test('class sessions paginate independently from event owner and preserve unknown schedule',async({page})=>{
  const requested:string[]=[];
  await page.route('**/api/v1/centers/*/activity-sources/*',route=>{
    const url=route.request().url();requested.push(url);
    expect(route.request().method()).toBe('GET');
    const isEvent=url.includes('/events');
    const older=url.includes('beforeSourceId='+uuid(3));
    const records=isEvent?[{sourceId:uuid(91),title:'رویداد محلی',
      classId:null,classTitle:null,scheduledAt:null}]:
      (older?[2,1]:Array.from({length:20},(_,i)=>22-i)).map(n=>({
        sourceId:uuid(n),title:'جلسه '+n,classId:uuid(99),
        classTitle:'کلاس مادر',scheduledAt:null
      }));
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({sourceKind:isEvent?'CENTER_EVENT':'CLASS_SESSION',records,
        nextBeforeSourceId:isEvent||older?null:uuid(3)})});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
  await expect(page.getByText('جلسه 22')).toBeVisible();
  await expect(page.getByText('زمان برنامه‌ریزی: نامشخص').first()).toBeVisible();
  await page.getByRole('button',{name:'منابع قدیمی‌تر'}).click();
  await expect(page.getByText('جلسه 1',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'دریافت رویدادها'}).click();
  await expect(page.getByText('رویداد محلی')).toBeVisible();
  await expect(page.getByText('جلسه 22')).toHaveCount(0);
  expect(requested).toHaveLength(3);
  expect(requested[1]).toContain('beforeSourceId='+uuid(3));
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByText('رویداد محلی')).toHaveCount(0);
});

test('unapproved source operation is denied and shows no source identity',async({page})=>{
  await page.route('**/api/v1/centers/*/activity-sources/*',route=>
    route.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
  await open(page);
  await page.getByRole('button',{name:'دریافت رویدادها'}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده جلسات و رویدادها');
  await expect(page.getByText(/شناسه منبع:/)).toHaveCount(0);
});

test('invalid source identity and mismatched source type fail closed',async({page})=>{
  await page.route('**/api/v1/centers/*/activity-sources/*',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sourceKind:'CLASS_SESSION',records:[{sourceId:'not-a-uuid',title:'Forged',
        classId:null,classTitle:null,scheduledAt:null}],nextBeforeSourceId:null
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت رویدادها'}).click();
  await expect(page.getByRole('alert')).toContainText('دریافت اطلاعات جلسات و رویدادها ممکن نیست');
  await expect(page.getByText('Forged')).toHaveCount(0);
});
