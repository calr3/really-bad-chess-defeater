//! .NET's `Span<T>.Sort(Comparison<T>)` (ArraySortHelper<T>.IntrospectiveSort), step for step.
//!
//! The game sorts candidate moves with this unstable sort, and the order it leaves equal moves in
//! decides which one the engine plays. A different sort algorithm would change the game's moves.

/// Sort `keys` exactly as .NET 8 does. `cmp` returns <0, 0 or >0 like a .NET Comparison.
pub fn dotnet_sort<T: Copy>(keys: &mut [T], cmp: &impl Fn(&T, &T) -> i32) {
    if keys.len() > 1 {
        let depth = 2 * (log2(keys.len() as u32) + 1);
        intro_sort(keys, depth, cmp);
    }
}

fn log2(v: u32) -> i32 {
    31 - v.leading_zeros() as i32
}

const SIZE_THRESHOLD: usize = 16;

fn intro_sort<T: Copy>(keys: &mut [T], mut depth: i32, cmp: &impl Fn(&T, &T) -> i32) {
    let mut size = keys.len();
    while size > 1 {
        if size <= SIZE_THRESHOLD {
            if size == 2 {
                swap_if_greater(keys, cmp, 0, 1);
                return;
            }
            if size == 3 {
                swap_if_greater(keys, cmp, 0, 1);
                swap_if_greater(keys, cmp, 0, 2);
                swap_if_greater(keys, cmp, 1, 2);
                return;
            }
            insertion_sort(&mut keys[..size], cmp);
            return;
        }
        if depth == 0 {
            heap_sort(&mut keys[..size], cmp);
            return;
        }
        depth -= 1;
        let p = pick_pivot_and_partition(&mut keys[..size], cmp);
        intro_sort(&mut keys[p + 1..size], depth, cmp);
        size = p;
    }
}

fn swap_if_greater<T: Copy>(keys: &mut [T], cmp: &impl Fn(&T, &T) -> i32, i: usize, j: usize) {
    if cmp(&keys[i], &keys[j]) > 0 {
        keys.swap(i, j);
    }
}

fn pick_pivot_and_partition<T: Copy>(keys: &mut [T], cmp: &impl Fn(&T, &T) -> i32) -> usize {
    let hi = keys.len() - 1;
    let middle = hi >> 1;
    swap_if_greater(keys, cmp, 0, middle);
    swap_if_greater(keys, cmp, 0, hi);
    swap_if_greater(keys, cmp, middle, hi);
    let pivot = keys[middle];
    keys.swap(middle, hi - 1);
    let (mut left, mut right) = (0usize, hi - 1);
    while left < right {
        loop {
            left += 1;
            if cmp(&keys[left], &pivot) >= 0 {
                break;
            }
        }
        loop {
            right -= 1;
            if cmp(&pivot, &keys[right]) >= 0 {
                break;
            }
        }
        if left >= right {
            break;
        }
        keys.swap(left, right);
    }
    if left != hi - 1 {
        keys.swap(left, hi - 1);
    }
    left
}

fn heap_sort<T: Copy>(keys: &mut [T], cmp: &impl Fn(&T, &T) -> i32) {
    let n = keys.len();
    let mut i = n >> 1;
    while i >= 1 {
        down_heap(keys, i, n, cmp);
        i -= 1;
    }
    let mut i = n;
    while i > 1 {
        keys.swap(0, i - 1);
        down_heap(keys, 1, i - 1, cmp);
        i -= 1;
    }
}

fn down_heap<T: Copy>(keys: &mut [T], mut i: usize, n: usize, cmp: &impl Fn(&T, &T) -> i32) {
    let d = keys[i - 1];
    while i <= n >> 1 {
        let mut child = 2 * i;
        if child < n && cmp(&keys[child - 1], &keys[child]) < 0 {
            child += 1;
        }
        if cmp(&d, &keys[child - 1]) >= 0 {
            break;
        }
        keys[i - 1] = keys[child - 1];
        i = child;
    }
    keys[i - 1] = d;
}

fn insertion_sort<T: Copy>(keys: &mut [T], cmp: &impl Fn(&T, &T) -> i32) {
    for i in 0..keys.len() - 1 {
        let t = keys[i + 1];
        let mut j = i as isize;
        while j >= 0 && cmp(&t, &keys[j as usize]) < 0 {
            keys[j as usize + 1] = keys[j as usize];
            j -= 1;
        }
        keys[(j + 1) as usize] = t;
    }
}
