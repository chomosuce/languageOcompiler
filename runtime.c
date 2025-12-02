#include <stdint.h>
#include <stdlib.h>

typedef struct Array {
    int32_t length;
    void **data;
} Array;

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
