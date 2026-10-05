// Game Vault — aşamalı iyileştirmeler. Uygulama JavaScript olmadan da çalışır.
(function () {
    'use strict';

    // Bozuk ya da yüklenemeyen kapak görsellerinde baş harfli yedek görünüm gösterilir.
    function markBroken(img) {
        var cover = img.closest('.cover');
        if (cover) {
            cover.classList.add('is-broken');
        }
    }

    document.querySelectorAll('img[data-cover]').forEach(function (img) {
        if (img.complete && img.naturalWidth === 0) {
            markBroken(img);
        } else {
            img.addEventListener('error', function () { markBroken(img); }, { once: true });
        }
    });

    // Gönderilen formda düğmeyi kilitle ve "yükleniyor" durumunu göster (çift gönderimi önler).
    document.querySelectorAll('form[data-busy-form]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            var button = event.submitter || form.querySelector('button[type="submit"]');
            if (!button || button.getAttribute('aria-busy') === 'true') {
                return;
            }
            var busyText = button.getAttribute('data-busy-text');
            button.setAttribute('aria-busy', 'true');
            // disabled, düğmenin kendisini gönderimden çıkaracağı için bir sonraki çerçevede uygulanır.
            window.setTimeout(function () {
                button.disabled = true;
                if (busyText) {
                    button.textContent = busyText;
                }
            }, 0);
        });
    });

    // Filtreler masaüstünde her zaman açık.
    var filterDetails = document.querySelector('[data-filter-details]');
    if (filterDetails && window.matchMedia('(min-width: 901px)').matches) {
        filterDetails.open = true;
    }

    // Kapak seçimi "Adres" değilse adres alanını gizle.
    var picker = document.querySelector('[data-cover-picker]');
    var urlField = document.querySelector('[data-cover-url]');
    if (picker && urlField) {
        var update = function () {
            var selected = picker.querySelector('input[type="radio"]:checked');
            urlField.hidden = !selected || selected.value !== 'url';
        };
        picker.addEventListener('change', update);
        update();
    }
})();
