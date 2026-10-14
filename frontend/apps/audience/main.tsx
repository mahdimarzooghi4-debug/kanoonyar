import React from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';
import { PublicNationalPrograms } from './PublicNationalPrograms';

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Shell title="پرتال مخاطبان">
      <PublicNationalPrograms />
    </Shell>
  </React.StrictMode>,
);
