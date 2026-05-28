import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import { en } from './locales/en';
import { bg } from './locales/bg';

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      bg: { translation: bg }
    },
    fallbackLng: 'en',
    supportedLngs: ['en', 'bg'],
    interpolation: { escapeValue: false }
  });

export default i18n;
