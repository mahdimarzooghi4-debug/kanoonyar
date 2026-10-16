import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center-only reporting');
const session='10000000-0000-4000-8000-000000000019';
async function open(page:import('@playwright/test').Page){
  await page.route('**/api/v1/identity/center-memberships',route=>route.fulfill({
    status:200,contentType:'application/json',
    body:JSON.stringify({memberships:[
      {centerId:'c1',displayName:'کانون اول',roleCode:'member'},
      {centerId:'c2',displayName:'کانون دوم',roleCode:'member'}]})
  }));
  await page.route('**/api/v1/centers/*/activity-sources/class-sessions',route=>
    route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({sourceKind:'CLASS_SESSION',records:[{
        sourceId:session,title:'جلسه نمونه',
        classId:'20000000-0000-4000-8000-000000000022',
        classTitle:'کلاس نمونه',scheduledAt:null
      }],nextBeforeSourceId:null})}));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-token');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
  await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
  await page.getByRole('button',{name:'گزارش وقوع و حضور جلسه'}).click();
}
test('claim source chain is unverified and zero unknown attendance is never inferred',async({page})=>{
  const commands:{url:string,body:Record<string,unknown>}[]=[];
  await page.route('**/api/v1/centers/*/class-sessions/*/*-claims',route=>{
    const body=route.request().postDataJSON() as Record<string,unknown>;
    commands.push({url:route.request().url(),body});
    expect(route.request().method()).toBe('POST');
    if(route.request().url().endsWith('/occurrence-claims'))
      return route.fulfill({status:200,contentType:'application/json',
        body:JSON.stringify({occurrenceReportId:body.occurrenceReportId,sessionId:session,
        verificationStatus:'UNVERIFIED'})});
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({claimId:body.claimId,sessionId:session,
      occurrenceReportId:body.occurrenceReportId,verificationStatus:'UNVERIFIED'})});
  });
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('گزارش آزمایشی');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByText('گزارش ثبت شد؛ راستی‌آزمایی‌نشده است.')).toBeVisible();
  await page.getByRole('textbox',{name:'شناسه مستعار مخاطب'}).fill('opaque-test-only');
  await expect(page.getByRole('button',{name:'ثبت ادعای حضور/غیاب'})).toBeDisabled();
  await page.getByRole('combobox',{name:'وضعیت ادعاشده حضور'}).selectOption('REPORTED_PRESENT');
  await page.getByRole('button',{name:'ثبت ادعای حضور/غیاب'}).click();
  await expect(page.getByText('ادعای حضور/غیاب ثبت شد؛ تأیید نشده است.')).toBeVisible();
  expect(commands).toHaveLength(2);
  expect(commands[0].body.claimedStartedAt).toBeNull();
  expect(commands[1].body.occurrenceReportId).toBe(commands[0].body.occurrenceReportId);
  expect(commands[1].body.reportedPresence).toBe('REPORTED_PRESENT');
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByRole('region',{name:'گزارش وقوع و حضور جلسه'})).toHaveCount(0);
});
test('denied occurrence never creates attendance UI',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>
    route.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('مورد محدود');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل گزارش وقوع جلسه');
  await expect(page.getByRole('textbox',{name:'شناسه مستعار مخاطب'})).toHaveCount(0);
});
test('server VERIFIED response cannot be mistaken for legitimate claim confirmation',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      occurrenceReportId:'00000000-0000-4000-8000-000000000000',sessionId:session,
      verificationStatus:'VERIFIED'
    })}));
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('ادعای مشکوک');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('ثبت گزارش وقوع جلسه تأیید نشد');
  await expect(page.getByRole('textbox',{name:'شناسه مستعار مخاطب'})).toHaveCount(0);
});


const historyOccurrenceId='30000000-0000-4000-8000-000000000031';
const historyTimestamp=(n:number)=>'2026-10-16T18:00:00.'+String(n).padStart(6,'0')+'Z';
function historySortKey(value:string){
  const match=/^(.*\.)(\d{1,7})Z$/.exec(value);
  return match?match[1]+match[2].padEnd(7,'0')+'Z':value;
}
function historyClaims(){
  return Array.from({length:43},(_,index)=>{
    const n=index+1;
    const reverseId=44-n;
    return {
      claimId:'40000000-0000-4000-8000-'+String(reverseId).padStart(12,'0'),
      participantRef:'synthetic-participant-'+String(n).padStart(2,'0'),
      reportedPresence:'REPORTED_PRESENT' as const,
      recordedAt:n===1?'2026-10-16T18:00:00.0000010Z':
        n===2?'2026-10-16T18:00:00.000001Z':historyTimestamp(n),
      verificationStatus:'UNVERIFIED' as const
    };
  }).sort((left,right)=>historySortKey(left.recordedAt)===historySortKey(right.recordedAt)?
    (left.claimId>right.claimId?-1:left.claimId<right.claimId?1:0):
    (historySortKey(left.recordedAt)>historySortKey(right.recordedAt)?-1:1));
}
async function routeOccurrenceHistory(page:import('@playwright/test').Page){
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>{
    if(route.request().method()!=='GET')return route.continue();
    return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,
      occurrenceReports:[{
        occurrenceReportId:historyOccurrenceId,reportedNote:'گزارش تاریخچه نمونه',
        evidenceRef:null,claimedStartedAt:null,claimedEndedAt:null,
        recordedAt:'2026-10-16T18:00:00.000000Z',verificationStatus:'UNVERIFIED'
      }],
      nextBeforeAt:null,nextBeforeId:null
    })});
  });
}

test('history pages preserve every claim and send the exact keyset cursor',async({page})=>{
  const claims=historyClaims();
  const pages=[claims.slice(0,20),claims.slice(20,40),claims.slice(40)];
  const cursors:{beforeAt:string|null,beforeId:string|null}[]=[];
  let pageIndex=0;
  await routeOccurrenceHistory(page);
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims/*/attendance-claims**',route=>{
    const url=new URL(route.request().url());
    const beforeAt=url.searchParams.get('beforeAt');
    const beforeId=url.searchParams.get('beforeId');
    cursors.push({beforeAt,beforeId});
    const rows=pages[pageIndex++];
    const hasMore=pageIndex<pages.length;
    return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReportId:historyOccurrenceId,
      attendanceClaims:rows,
      nextBeforeAt:hasMore?rows[rows.length-1].recordedAt:null,
      nextBeforeId:hasMore?rows[rows.length-1].claimId:null
    })});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await expect(page.getByText('گزارش تاریخچه نمونه',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'مشاهده ادعاهای حضور این گزارش'}).click();
  const history=page.getByRole('region',{name:'ادعاهای حضور گزارش'});
  await expect(history.getByRole('listitem')).toHaveCount(20);
  await history.getByRole('button',{name:'ادعاهای حضور قدیمی‌تر'}).click();
  await expect(history.getByRole('listitem')).toHaveCount(40);
  await history.getByRole('button',{name:'ادعاهای حضور قدیمی‌تر'}).click();
  await expect(history.getByRole('listitem')).toHaveCount(43);
  await expect(history.getByRole('button',{name:'ادعاهای حضور قدیمی‌تر'})).toHaveCount(0);
  const rendered=await history.getByRole('listitem').allTextContents();
  expect(new Set(rendered).size).toBe(43);
  expect(rendered.every(text=>text.includes('— تأییدنشده'))).toBeTruthy();
  expect(cursors).toEqual([
    {beforeAt:null,beforeId:null},
    {beforeAt:claims[19].recordedAt,beforeId:claims[19].claimId},
    {beforeAt:claims[39].recordedAt,beforeId:claims[39].claimId}
  ]);
});

test('history rejects a continuation cursor that does not match the final row',async({page})=>{
  const claims=historyClaims().slice(0,20);
  await routeOccurrenceHistory(page);
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims/*/attendance-claims**',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReportId:historyOccurrenceId,
      attendanceClaims:claims,
      nextBeforeAt:claims[19].recordedAt,
      nextBeforeId:'ffffffff-ffff-4fff-8fff-ffffffffffff'
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await page.getByRole('button',{name:'مشاهده ادعاهای حضور این گزارش'}).click();
  const history=page.getByRole('region',{name:'ادعاهای حضور گزارش'});
  await expect(history.getByRole('alert')).toContainText('دریافت تاریخچه حضور و غیاب ممکن نیست');
  await expect(history.getByText('synthetic-participant-43')).toHaveCount(0);
});

test('history rejects duplicate claim IDs before rendering a malformed page',async({page})=>{
  const claims=historyClaims().slice(0,20);
  claims[1]={...claims[1],claimId:claims[0].claimId};
  await routeOccurrenceHistory(page);
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims/*/attendance-claims**',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReportId:historyOccurrenceId,
      attendanceClaims:claims,nextBeforeAt:null,nextBeforeId:null
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await page.getByRole('button',{name:'مشاهده ادعاهای حضور این گزارش'}).click();
  const history=page.getByRole('region',{name:'ادعاهای حضور گزارش'});
  await expect(history.getByRole('alert')).toContainText('دریافت تاریخچه حضور و غیاب ممکن نیست');
  await expect(history.getByRole('listitem')).toHaveCount(0);
});

test('history rejects non-descending occurrence pages before rendering',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>{
    if(route.request().method()!=='GET')return route.continue();
    return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,
      occurrenceReports:[
        {occurrenceReportId:'50000000-0000-4000-8000-000000000001',
         reportedNote:'ردیف دوم',evidenceRef:null,claimedStartedAt:null,claimedEndedAt:null,
         recordedAt:'2026-10-16T18:00:00.000000Z',verificationStatus:'UNVERIFIED'},
        {occurrenceReportId:'50000000-0000-4000-8000-000000000002',
         reportedNote:'ردیف اول',evidenceRef:null,claimedStartedAt:null,claimedEndedAt:null,
         recordedAt:'2026-10-16T18:00:00.000000Z',verificationStatus:'UNVERIFIED'}
      ],
      nextBeforeAt:null,nextBeforeId:null
    })});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('دریافت تاریخچه وقوع جلسه ممکن نیست');
  await expect(page.getByText('ردیف دوم',{exact:true})).toHaveCount(0);
});

test('history rejects out-of-order attendance rows with unique IDs',async({page})=>{
  const claims=historyClaims().slice(0,20);
  [claims[0],claims[1]]=[claims[1],claims[0]];
  await routeOccurrenceHistory(page);
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims/*/attendance-claims**',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReportId:historyOccurrenceId,
      attendanceClaims:claims,nextBeforeAt:null,nextBeforeId:null
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await page.getByRole('button',{name:'مشاهده ادعاهای حضور این گزارش'}).click();
  const history=page.getByRole('region',{name:'ادعاهای حضور گزارش'});
  await expect(history.getByRole('alert')).toContainText('دریافت تاریخچه حضور و غیاب ممکن نیست');
  await expect(history.getByRole('listitem')).toHaveCount(0);
});
