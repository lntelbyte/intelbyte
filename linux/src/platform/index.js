import {
  setupApps,
  removeOverrides,
  removeShims,
  runApp,
  runAppArgv,
  inspectAppState,
  runningUnprotected,
  killApp,
} from './linux/apps.js';
import {
  scrubChromium,
  isBrowserRunning,
  installedChromiumBrowsers,
} from './linux/chromium.js';
import * as firefox from './linux/firefox.js';

if (process.platform !== 'linux') {
  console.error(
    `intelbyte (Linux edition) can't run on "${process.platform}". ` +
      'Use the Windows edition on Windows.'
  );
  process.exit(1);
}

export default {
  setupApps,
  unwire(registry) {
    return { overrides: removeOverrides(registry), shims: removeShims(registry) };
  },
  runApp,
  runAppArgv,
  inspectAppState,
  runningUnprotected,
  killApp,
  installedChromiumBrowsers,
  isBrowserRunning,
  scrubChromium,
  firefox,
};
