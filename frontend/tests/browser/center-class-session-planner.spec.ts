import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only class workflow');

async function start(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships',route=>route.fulfill({
    status:200,contentType:'application/json',
    body:'{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"},{"centerId":"c2","displayName":"کانون دو","roleCode":"member"}]}'
  }));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-bearer');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}

test('create class then schedule two sessions with verified server acknowledgement',async({page})=>{
  const posted:{route:string,body:Record<string,unknown>}[]=[];
  await page.route('**/api/v1/centers/*/classes*',async route=>{
    const body=route.request().postDataJSON() as Record<string,unknown>;
    posted.push({route:route.request().url(),body});
    expect(route.request().method()).toBe('POST');
    expect(route.request().headers().authorization).toBe('Bearer synthetic-bearer');
    if(route.request().url().endsWith('/classes'))
      return route.fulfill({status:200,contentType:'application/json',
        body:JSON.stringify({classId:body.classId})});
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({sessionId:body.sessionId,classId:body.classId,
        scheduledAt:body.scheduledAt})});
  });
  await start(page);
  await page.getByRole('textbox',{name:'عنوان کلاس جدید'}).fill('کلاس نمونه');
  await page.getByRole('button',{name:'ثبت کلاس',exact:true}).click();
  await expect(page.getByText('کلاس توسط API ثبت شد.',{exact:false})).toBeVisible();
  await page.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه اول');
  await page.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
  await expect(page.getByText('جلسه برنامه‌ریزی‌شده در API ثبت شد؛ برگزاری تأیید نشده است.')).toBeVisible();
  expect(posted).toHaveLength(2);
  expect(posted[0].body.title).toBe('کلاس نمونه');
  expect(posted[1].body.title).toBe('جلسه اول');
  expect(posted[1].body.scheduledAt).toBeNull();
  expect(posted[1].body.classId).toBe(posted[0].body.classId);
  await page.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه دوم');
  await page.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
  expect(posted).toHaveLength(3);
  expect(posted[2].body.sessionId).not.toBe(posted[1].body.sessionId);
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByRole('textbox',{name:'عنوان کلاس جدید'})).toHaveValue('');
  await expect(page.getByRole('textbox',{name:'عنوان جلسه جدید'})).toHaveCount(0);
});

test('denied class mutation cannot be interpreted as created or scheduled',async({page})=>{
  await page.route('**/api/v1/centers/*/classes',route=>
    route.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
  await start(page);
  await page.getByRole('textbox',{name:'عنوان کلاس جدید'}).fill('بدون مجوز');
  await page.getByRole('button',{name:'ثبت کلاس',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل ثبت کلاس تأیید نشده');
  await expect(page.getByRole('textbox',{name:'عنوان جلسه جدید'})).toHaveCount(0);
});

test('incorrect server class acknowledgement fails closed',async({page})=>{
  await page.route('**/api/v1/centers/*/classes',route=>
    route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({classId:'fake-unrelated-id'})}));
  await start(page);
  await page.getByRole('textbox',{name:'عنوان کلاس جدید'}).fill('مشکوک');
  await page.getByRole('button',{name:'ثبت کلاس',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('ثبت کلاس تأیید نشد');
  await expect(page.getByRole('textbox',{name:'عنوان جلسه جدید'})).toHaveCount(0);
});
