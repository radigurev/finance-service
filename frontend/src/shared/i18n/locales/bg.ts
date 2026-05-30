export const bg = {
  app: {
    title: 'Финанси'
  },
  nav: {
    section: 'Счетоводство',
    accounts: 'Сметкоплан',
    currencies: 'Валути',
    exchangeRates: 'Валутни курсове'
  },
  layout: {
    compact: 'Компактна гъстота',
    comfortable: 'Свободна гъстота',
    densityToggle: 'Превключи гъстотата',
    languageToggle: 'Смени езика'
  },
  common: {
    save: 'Запази',
    saving: 'Запазване…',
    cancel: 'Отказ',
    confirm: 'Потвърди',
    edit: 'Редактирай',
    delete: 'Изтрий',
    close: 'Затвори',
    back: 'Назад'
  },
  filter: {
    searchPlaceholder: 'Търсене…'
  },
  table: {
    rowsPerPage: 'Редове на страница'
  },
  auth: {
    login: 'Вход',
    username: 'Потребител',
    password: 'Парола',
    submit: 'Вход',
    logout: 'Изход'
  },
  accounts: {
    title: 'Сметкоплан',
    newAccount: 'Нова сметка',
    createTitle: 'Нова сметка',
    editTitle: 'Редакция на сметка',
    code: 'Код',
    name: 'Наименование',
    type: 'Тип',
    parent: 'Родителска',
    active: 'Активна',
    country: 'Държава',
    statusActive: 'Активна',
    statusInactive: 'Неактивна',
    created: 'Сметката е създадена.',
    updated: 'Сметката е обновена.',
    empty: 'Все още няма сметки.',
    emptyHint: 'Създайте първата сметка, за да изградите сметкоплана.',
    type_Asset: 'Актив',
    type_Liability: 'Пасив',
    type_Equity: 'Собствен капитал',
    type_Revenue: 'Приход',
    type_Expense: 'Разход',
    validation: {
      codeRequired: 'Кодът е задължителен.',
      codeTooLong: 'Кодът трябва да е до 20 символа.',
      nameRequired: 'Наименованието е задължително.',
      nameTooLong: 'Наименованието трябва да е до 200 символа.'
    }
  },
  currencies: {
    title: 'Валути',
    newCurrency: 'Нова валута',
    createTitle: 'Нова валута',
    editTitle: 'Редакция на валута',
    searchPlaceholder: 'Търсене по код или наименование…',
    isoCode: 'ISO код',
    name: 'Наименование',
    symbol: 'Символ',
    active: 'Активна',
    statusActive: 'Активна',
    statusInactive: 'Неактивна',
    created: 'Валутата е създадена.',
    updated: 'Валутата е обновена.',
    deactivated: 'Валутата е деактивирана.',
    deactivate: 'Деактивирай',
    deactivateTitle: 'Деактивиране на валута',
    deactivateMessage: 'Да се деактивира ли {{code}}? Тя няма да се показва в списъците за избор, но историческите записи я запазват.',
    empty: 'Все още няма валути.',
    emptyHint: 'Добавете първата валута, за да започнете да записвате суми.',
    validation: {
      isoCodeInvalid: 'ISO кодът трябва да е точно три главни латински букви.',
      nameRequired: 'Наименованието е задължително.',
      nameTooLong: 'Наименованието трябва да е до 100 символа.',
      symbolTooLong: 'Символът трябва да е до 5 символа.'
    }
  },
  exchangeRates: {
    title: 'Валутни курсове',
    queryOverline: 'Заявка',
    queryHeading: 'Справка за курс',
    resultOverline: 'Резултат',
    latestHeading: 'Последен курс',
    modeLatest: 'Към дата',
    modeRange: 'За период',
    currency: 'Валута',
    asOfDate: 'Към дата',
    from: 'От',
    to: 'До',
    date: 'Дата',
    rate: 'Курс',
    asOf: 'Към {{date}}',
    invalidRange: 'Началната дата трябва да е преди или равна на крайната.',
    noSelectionTitle: 'Все още няма курс за показване.',
    noSelectionLatestHint: 'Изберете валута и дата, за да видите последния курс към нея.',
    noSelectionRangeHint: 'Изберете валута и валиден период, за да видите курсовете.'
  },
  errors: {
    GENERIC_ERROR: 'Възникна грешка. Моля, опитайте отново.',
    VALIDATION_FAILED: 'Някои полета са невалидни. Моля, прегледайте и опитайте отново.',
    CONCURRENT_MODIFICATION: 'Записът е променен от друг потребител. Презаредете и опитайте отново.',
    PAGE_SIZE_TOO_LARGE: 'Заявеният размер на страницата е твърде голям.',
    INVALID_FILTER_FIELD: 'Това поле не може да се филтрира.',
    INVALID_SORT_FIELD: 'Това поле не може да се сортира.',
    INVALID_OPERATOR: 'Невалиден оператор за филтриране.',
    INVALID_FILTER_VALUE: 'Невалидна стойност за филтриране.',
    INVALID_CREDENTIALS: 'Невалидно потребителско име или парола.',
    DUPLICATE_ACCOUNT_CODE: 'Сметка с този код вече съществува.',
    INVALID_PARENT_ACCOUNT: 'Невалидна родителска сметка.',
    ACCOUNT_NOT_FOUND: 'Сметката не е намерена.',
    INVALID_ACCOUNT_CODE: 'Невалиден код на сметка.',
    INVALID_ACCOUNT_TYPE: 'Невалиден тип на сметка.',
    ACCOUNT_INACTIVE: 'Сметката е неактивна.',
    ACCOUNT_HAS_ENTRIES: 'Сметката има осчетоводени записи и не може да бъде изтрита.',
    INVALID_CURRENCY_CODE: 'Кодът на валутата трябва да е точно три главни латински букви.',
    DUPLICATE_CURRENCY_CODE: 'Валута с този код вече съществува.',
    CURRENCY_NOT_FOUND: 'Валутата не е намерена.',
    EXCHANGE_RATE_NOT_FOUND: 'Не е намерен валутен курс за избраната валута и дата.',
    INVALID_DATE_RANGE: 'Началната дата трябва да е преди или равна на крайната.',
    WAREHOUSE_NOMENCLATURE_UNREACHABLE: 'Номенклатурните данни са временно недостъпни. Моля, опитайте отново скоро.',
    INVALID_CURRENCY_NAME: 'Наименованието на валутата е задължително и трябва да е до 100 символа.',
    INVALID_CURRENCY_SYMBOL: 'Символът на валутата трябва да е до 5 символа.'
  }
};
