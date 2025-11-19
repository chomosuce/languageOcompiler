#include <stdint.h>
#include <stdlib.h>

typedef struct Array {
    int32_t length;
    void **data;
} Array;

typedef struct ListNode {
    void *value;
    struct ListNode *next;
} ListNode;

typedef struct List {
    ListNode *head;
} List;

static void *xcalloc(size_t count, size_t size)
{
    void *ptr = calloc(count, size);
    if (ptr == NULL) {
        abort();
    }
    return ptr;
}

static void *xmalloc(size_t size)
{
    void *ptr = malloc(size);
    if (ptr == NULL) {
        abort();
    }
    return ptr;
}

Array *o_array_new(int32_t length)
{
    if (length < 0) {
        length = 0;
    }

    Array *array = xmalloc(sizeof(Array));
    array->length = length;
    array->data = xcalloc((size_t)length, sizeof(void *));
    return array;
}

int32_t o_array_length(Array *array)
{
    if (array == NULL) {
        return 0;
    }

    return array->length;
}

static void ensure_array_bounds(Array *array, int32_t index)
{
    if (array == NULL || index < 0 || index >= array->length) {
        abort();
    }
}

void *o_array_get(Array *array, int32_t index)
{
    ensure_array_bounds(array, index);
    return array->data[index];
}

void o_array_set(Array *array, int32_t index, void *value)
{
    ensure_array_bounds(array, index);
    array->data[index] = value;
}

static List *allocate_list(void)
{
    List *list = xmalloc(sizeof(List));
    list->head = NULL;
    return list;
}

static ListNode *append_node(ListNode *head, void *value)
{
    ListNode *node = xmalloc(sizeof(ListNode));
    node->value = value;
    node->next = NULL;

    if (head == NULL) {
        return node;
    }

    ListNode *current = head;
    while (current->next != NULL) {
        current = current->next;
    }

    current->next = node;
    return head;
}

List *o_list_empty(void)
{
    return allocate_list();
}

List *o_list_singleton(void *value)
{
    List *list = allocate_list();
    list->head = append_node(NULL, value);
    return list;
}

List *o_list_replicate(void *value, int32_t count)
{
    if (count <= 0) {
        return o_list_empty();
    }

    List *list = allocate_list();
    ListNode *head = NULL;
    for (int32_t i = 0; i < count; ++i) {
        head = append_node(head, value);
    }

    list->head = head;
    return list;
}

List *o_list_append(List *list, void *value)
{
    if (list == NULL) {
        list = allocate_list();
    }

    list->head = append_node(list->head, value);
    return list;
}

void *o_list_head(List *list)
{
    if (list == NULL || list->head == NULL) {
        return NULL;
    }

    return list->head->value;
}

List *o_list_tail(List *list)
{
    List *result = allocate_list();

    if (list == NULL || list->head == NULL) {
        return result;
    }

    result->head = list->head->next;
    return result;
}

Array *o_list_to_array(List *list)
{
    int32_t length = 0;
    for (ListNode *node = list != NULL ? list->head : NULL; node != NULL; node = node->next) {
        ++length;
    }

    Array *array = o_array_new(length);
    int32_t index = 0;
    for (ListNode *node = list != NULL ? list->head : NULL; node != NULL; node = node->next) {
        array->data[index++] = node->value;
    }

    return array;
}
