'use strict';

const status = document.querySelector('#status');
const connect = document.querySelector('#connect');

connect.addEventListener('click', async () => {
  connect.disabled = true;
  status.textContent = 'Проверяю локальную программу…';
  try {
    const response = await fetch('http://127.0.0.1:8765/api/extension-health', {
      cache: 'no-store',
      targetAddressSpace: 'local'
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    status.textContent = 'Подключено. Обновите вкладку YouTube Music.';
    connect.textContent = 'Подключено';
  } catch {
    status.textContent = 'Связи нет. Запустите PixelCompanion.exe и разрешите Chrome доступ к локальной сети.';
    connect.disabled = false;
  }
});
