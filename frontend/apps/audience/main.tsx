import React from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';
import { PublicNationalPrograms } from './PublicNationalPrograms';
import { PublishedVersionReference } from './PublishedVersionReference';
import { AudienceIdentityBoundary } from './AudienceIdentityBoundary';

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Shell title="پرتال مخاطبان">
      <PublishedVersionReference />
      <PublicNationalPrograms />
      {import.meta.env.MODE === 'audience-ci' && <AudienceIdentityBoundary />}
    </Shell>
  </React.StrictMode>,
);
