using System;
using System.Linq;

namespace RestAgent
{
    public static class ParameterCatalog
    {
        // Query names are API-specific. This catalog is a set of examples, not
        // a registry of allowed names; arbitrary names and repeated rows work.
        public static readonly HeaderInfo[] Entries = new[]
        {
            Item("page", "Номер страницы"), Item("per_page", "Количество записей на странице"),
            Item("page_size", "Размер страницы"), Item("limit", "Максимальное количество записей"),
            Item("offset", "Количество пропускаемых записей"), Item("cursor", "Курсор следующей страницы"),
            Item("sort", "Поле сортировки"), Item("sort_by", "Поле сортировки"),
            Item("order", "Направление сортировки: asc / desc"), Item("order_by", "Правило сортировки"),
            Item("search", "Поисковый текст"), Item("q", "Поисковый текст"), Item("query", "Поисковое выражение"),
            Item("filter", "Фильтр"), Item("id", "Идентификатор записи"), Item("ids", "Несколько идентификаторов"),
            Item("status", "Статус; допустимые значения определяет API"),
            Item("date_from", "Начало периода"), Item("date_to", "Конец периода"),
            Item("created_from", "Начало периода создания"), Item("created_to", "Конец периода создания"),
            Item("updated_from", "Начало периода изменения"), Item("updated_to", "Конец периода изменения"),
            Item("createdAt", "Дата создания; смысл фильтра определяет API"),
            Item("createdFrom", "Начало периода создания (camelCase)"), Item("createdTo", "Конец периода создания (camelCase)"),
            Item("updatedAt", "Дата изменения; смысл фильтра определяет API"),
            Item("updatedFrom", "Начало периода изменения (camelCase)"), Item("updatedTo", "Конец периода изменения (camelCase)"),
            Item("start_date", "Начальная дата"), Item("end_date", "Конечная дата"),
            Item("fields", "Поля ответа"), Item("include", "Связанные сущности ответа"), Item("expand", "Расширенные поля ответа"),
            Item("format", "Формат ответа"), Item("locale", "Локаль"), Item("language", "Язык"),
            Item("category", "Категория"), Item("category_id", "Идентификатор категории"),
            Item("user_id", "Идентификатор пользователя"), Item("order_id", "Идентификатор заказа"),
            Item("sku", "Артикул товара"), Item("email", "Электронная почта"), Item("version", "Версия"),
            Item("after", "Записи после указанной границы"), Item("before", "Записи до указанной границы"),
            Item("since", "Начало периода или курсор"), Item("until", "Конец периода или курсор")
        }.OrderBy(h => h.Name, StringComparer.Ordinal).ToArray();
        static HeaderInfo Item(string name, string description) { return new HeaderInfo { Name = name, Description = description, Common = true }; }
    }
}
